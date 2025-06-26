using System;
using System.ComponentModel; // For Win32Exception
using System.IO;              // For FileStream
using System.Runtime.InteropServices; // For Marshal
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles; // For SafeFileHandle

namespace VpnApp
{
    /// <summary>
    /// Manages interaction with a TAP-Windows virtual network adapter.
    /// This class handles device discovery, opening, reading/writing IP packets,
    /// and controlling the adapter's media status (connected/disconnected).
    /// Requires Administrator privileges for most operations.
    /// </summary>
    /// <remarks>
    /// The class relies on P/Invoke calls to Windows SetupAPI for device discovery
    /// and Kernel32 for device I/O and control. Ensure the target system
    /// has a compatible TAP-Windows driver installed (e.g., from OpenVPN).
    /// </remarks>
    public class TapDevice : IDisposable
    {
        /// <summary>
        /// The NetCfgInstanceID (GUID string) of the TAP adapter, provided by the user.
        /// Example: "{xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx}"
        /// </summary>
        private readonly string _userProvidedTapInstanceGuid;

        /// <summary>
        /// The resolved system device path for the TAP adapter, used by CreateFile.
        /// Example: "\\\\?\\ROOT#NET#0000#{...}" (Symbolic link from SetupAPI)
        /// </summary>
        private string _actualDevicePath;

        /// <summary>
        /// Native handle to the opened TAP device.
        /// </summary>
        private SafeFileHandle _tapHandle;

        /// <summary>
        /// FileStream used for asynchronous I/O operations on the TAP device.
        /// </summary>
        private FileStream _tapStream;

        /// <summary>
        /// Action delegate for logging messages from this class.
        /// </summary>
        private readonly Action<string> _logMessageAction;

        /// <summary>
        /// Default buffer size for TAP packet reads/writes. Should accommodate MTU + headers.
        /// Ethernet MTU is typically 1500 bytes. TAP packets are raw IP packets.
        /// Max IP packet size is 65535, but usually much smaller.
        /// </summary>
        private const int DefaultPacketBufferSize = 4096; // A common practical size.

        /// <summary>
        /// Cached MAC address of the TAP adapter.
        /// </summary>
        private byte[] _macAddress;

        /// <summary>
        /// Gets a value indicating whether the TAP device is currently open and the stream is usable.
        /// </summary>
        public bool IsOpen => _tapHandle != null && !_tapHandle.IsInvalid && !_tapHandle.IsClosed &&
                              _tapStream != null && _tapStream.CanRead && _tapStream.CanWrite;

        /// <summary>
        /// Initializes a new instance of the <see cref="TapDevice"/> class.
        /// </summary>
        /// <param name="tapInstanceGuid">The NetCfgInstanceID (GUID string, e.g., "{...}") of the TAP adapter to use.</param>
        /// <param name="logMessageAction">Optional action for logging messages. If null, messages are logged to Console.WriteLine.</param>
        /// <exception cref="ArgumentNullException">Thrown if tapInstanceGuid is null or empty.</exception>
        public TapDevice(string tapInstanceGuid, Action<string> logMessageAction = null)
        {
            if (string.IsNullOrWhiteSpace(tapInstanceGuid))
                throw new ArgumentNullException(nameof(tapInstanceGuid), "TAP instance GUID cannot be null or empty.");

            _userProvidedTapInstanceGuid = tapInstanceGuid;
            _logMessageAction = logMessageAction ?? Console.WriteLine;
            TapDeviceEnumerator.InitializeLogger(_logMessageAction);
        }

        /// <summary>
        /// Opens the TAP device: finds its system path via SetupAPI, obtains a native handle using CreateFile,
        /// sets its media status to 'connected' via an IOCTL, and prepares a FileStream for asynchronous I/O.
        /// </summary>
        /// <returns>True if the device was opened successfully; otherwise, false.</returns>
        /// <remarks>
        /// This method requires Administrator privileges to succeed.
        /// Ensure the TAP driver is installed and the specified TAP adapter (by GUID) is enabled.
        /// </remarks>
        public bool Open()
        {
            if (IsOpen)
            {
                _logMessageAction?.Invoke("TAP device is already open.");
                return true;
            }

            _logMessageAction?.Invoke($"Attempting to find TAP device path for instance GUID: '{_userProvidedTapInstanceGuid}'");
            _actualDevicePath = TapDeviceEnumerator.GetDevicePathByInstanceGuid(_userProvidedTapInstanceGuid);

            if (string.IsNullOrEmpty(_actualDevicePath))
            {
                _logMessageAction?.Invoke($"Failed to find device path for TAP instance GUID: '{_userProvidedTapInstanceGuid}'. " +
                                          "Ensure TAP driver (e.g., OpenVPN TAP-Windows) is installed, " +
                                          "the adapter is enabled, and the provided GUID in settings is the correct NetCfgInstanceID (e.g. {{xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx}}).");
                return false;
            }
            _logMessageAction?.Invoke($"Found TAP device path: '{_actualDevicePath}'");

            try
            {
                // Open the device using its system path.
                // GENERIC_READ | GENERIC_WRITE: Required for bi-directional communication.
                // FILE_SHARE_READ | FILE_SHARE_WRITE: Allows other processes to also open the device (though typically one process controls a TAP adapter).
                // OPEN_EXISTING: Only open if the device exists.
                // FILE_ATTRIBUTE_SYSTEM: Common for system devices.
                // FILE_FLAG_OVERLAPPED: Essential for asynchronous I/O with FileStream.
                _tapHandle = NativeMethods.CreateFile(
                    _actualDevicePath,
                    NativeMethods.GENERIC_READ | NativeMethods.GENERIC_WRITE,
                    NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE,
                    IntPtr.Zero, // Default security attributes
                    NativeMethods.OPEN_EXISTING,
                    NativeMethods.FILE_ATTRIBUTE_SYSTEM | NativeMethods.FILE_FLAG_OVERLAPPED,
                    IntPtr.Zero); // No template file

                if (_tapHandle.IsInvalid)
                {
                    int errorCode = Marshal.GetLastWin32Error();
                    _logMessageAction?.Invoke($"Failed to open TAP device handle for '{_actualDevicePath}'. Win32 Error: {errorCode} - {new Win32Exception(errorCode).Message}. This often requires Administrator privileges or the TAP device may be in use or disabled.");
                    _tapHandle = null;
                    return false;
                }
                _logMessageAction?.Invoke($"Successfully opened TAP device handle: '{_actualDevicePath}'");

                // Attempt to get MAC Address. This also serves as an early test of IOCTL communication.
                _macAddress = GetMacAddressBytes(); // Will log success/failure internally
                if (_macAddress == null)
                {
                    _logMessageAction?.Invoke("Warning: Could not retrieve MAC address for the TAP adapter. IOCTL communication might be problematic. Continuing with Open operation.");
                    // This is not necessarily fatal for opening, so we continue.
                }

                // Set media status to 'connected'. This makes the OS see the virtual cable as plugged in.
                if (!SetMediaStatus(true))
                {
                     _logMessageAction?.Invoke("Open: Failed to set TAP media status to 'Connected'. This is critical. Closing device.");
                     CloseHandleAndStream(); // Clean up partially opened resources
                     return false;
                }
                // SetMediaStatus logs its own success/failure.

                // Create a FileStream from the handle for easier async Read/Write operations.
                // The bufferSize here is for the FileStream's internal buffer, not the packet size itself.
                _tapStream = new FileStream(_tapHandle, FileAccess.ReadWrite, bufferSize: DefaultPacketBufferSize, isAsync: true);
                _logMessageAction?.Invoke("FileStream for TAP device created. TAP device is now open and ready for I/O.");
                return true;
            }
            catch (Exception ex)
            {
                _logMessageAction?.Invoke($"Exception during TAP device Open: {ex.ToString()}"); // Log full exception
                CloseHandleAndStream();
                return false;
            }
        }

        /// <summary>
        /// Internal helper to close and dispose of the FileStream and SafeFileHandle.
        /// </summary>
        private void CloseHandleAndStream()
        {
            // _logMessageAction?.Invoke("Internal: Closing TAP FileStream and SafeFileHandle."); // Can be noisy
            try
            {
                _tapStream?.Dispose(); // This should also close the underlying SafeFileHandle if FileStream owns it.
            }
            catch (Exception ex)
            {
                _logMessageAction?.Invoke($"Exception disposing FileStream: {ex.Message}");
            }
            finally
            {
                _tapStream = null;
            }

            // If FileStream didn't own the handle, or if its Dispose failed to close the handle,
            // or if _tapHandle was opened but _tapStream creation failed, ensure _tapHandle is disposed.
            if (_tapHandle != null && !_tapHandle.IsInvalid)
            {
                if (!_tapHandle.IsClosed)
                {
                    try
                    {
                        _tapHandle.Dispose();
                    }
                    catch (Exception ex)
                    {
                        _logMessageAction?.Invoke($"Exception disposing SafeFileHandle: {ex.Message}");
                    }
                }
            }
            _tapHandle = null;
        }

        /// <summary>
        /// Sets the media status of the TAP device (virtual cable connected/disconnected).
        /// </summary>
        /// <param name="isConnected">True to set status to connected, false for disconnected.</param>
        /// <returns>True if the operation was successful; otherwise, false.</returns>
        /// <remarks>
        /// Requires the device handle to be open and valid. Uses TAP_IOCTL_SET_MEDIA_STATUS.
        /// This operation is crucial for the OS to recognize the interface as active or inactive.
        /// </remarks>
        public bool SetMediaStatus(bool isConnected)
        {
            if (_tapHandle == null || _tapHandle.IsInvalid || _tapHandle.IsClosed)
            {
                _logMessageAction?.Invoke("SetMediaStatus: TAP handle is not valid or closed. Cannot set status.");
                return false;
            }

            int status = isConnected ? 1 : 0; // Value expected by TAP_IOCTL_SET_MEDIA_STATUS
            uint bytesReturned; // Not typically used for output with this IOCTL, but required by DeviceIoControl.

            _logMessageAction?.Invoke($"Attempting to set media status to {(isConnected ? "Connected" : "Disconnected")} (value: {status}).");

            // Call DeviceIoControl using the overload for integer input.
            bool success = NativeMethods.DeviceIoControl_Input(
                _tapHandle,
                NativeMethods.TAP_IOCTL_SET_MEDIA_STATUS,
                ref status,      // Input buffer (the integer status value)
                sizeof(int),     // Size of input buffer
                IntPtr.Zero,     // Output buffer (not used for this IOCTL)
                0,               // Size of output buffer
                out bytesReturned,
                IntPtr.Zero);    // Overlapped structure (synchronous call)

            if (success)
            {
                _logMessageAction?.Invoke($"Successfully set media status to {(isConnected ? "Connected" : "Disconnected")}.");
                return true;
            }
            else
            {
                int errorCode = Marshal.GetLastWin32Error();
                _logMessageAction?.Invoke($"Failed to set media status. Win32 Error Code: {errorCode} - {new Win32Exception(errorCode).Message}");
                return false;
            }
        }

        /// <summary>
        /// Gets the MAC address of the TAP adapter.
        /// </summary>
        /// <returns>A 6-byte array representing the MAC address, or null if retrieval fails.</returns>
        /// <remarks>
        /// Uses TAP_IOCTL_GET_MAC. The result is cached after the first successful call.
        /// Requires the device handle to be open and valid.
        /// </remarks>
        public byte[] GetMacAddressBytes()
        {
            // Return cached MAC address if already retrieved.
            if (_macAddress != null) return (byte[])_macAddress.Clone(); // Return a clone to prevent external modification

            if (_tapHandle == null || _tapHandle.IsInvalid || _tapHandle.IsClosed)
            {
                _logMessageAction?.Invoke("GetMacAddressBytes: TAP handle is not valid or closed. Cannot get MAC address.");
                return null;
            }

            byte[] macBuffer = new byte[6]; // MAC addresses are 6 bytes.
            uint bytesReturned;

            _logMessageAction?.Invoke("Attempting to get MAC address using TAP_IOCTL_GET_MAC.");
            bool success = NativeMethods.DeviceIoControl_OutputBytes(
                _tapHandle,
                NativeMethods.TAP_IOCTL_GET_MAC,
                IntPtr.Zero, // No input buffer for this IOCTL
                0,
                macBuffer,   // Output buffer to receive the MAC address
                (uint)macBuffer.Length,
                out bytesReturned,
                IntPtr.Zero); // Synchronous call

            if (success && bytesReturned == 6)
            {
                _logMessageAction?.Invoke($"Successfully retrieved MAC address: {BitConverter.ToString(macBuffer).Replace("-", ":")}");
                _macAddress = macBuffer; // Cache the retrieved MAC address
                return (byte[])_macAddress.Clone(); // Return a clone
            }
            else
            {
                int errorCode = Marshal.GetLastWin32Error();
                _logMessageAction?.Invoke($"Failed to get MAC address. IOCTL Success: {success}, BytesReturned: {bytesReturned}, Win32 Error: {errorCode} - {new Win32Exception(errorCode).Message}");
                return null;
            }
        }

        /// <summary>
        /// Asynchronously reads an IP packet from the TAP device.
        /// </summary>
        /// <param name="buffer">The buffer to write the packet data into.</param>
        /// <param name="offset">The byte offset in buffer at which to begin writing data read from the stream.</param>
        /// <param name="count">The maximum number of bytes to read (should be large enough for expected packets).</param>
        /// <param name="token">The token to monitor for cancellation requests.</param>
        /// <returns>
        /// A task that represents the asynchronous read operation. The value of the TResult parameter contains
        /// the total number of bytes read into the buffer. This can be less than the number of bytes requested
        /// if that many bytes are not currently available, or zero (0) if the end of the stream has been reached
        /// (though for TAP devices, it usually means no packet was available at that moment or an error).
        /// </returns>
        /// <exception cref="InvalidOperationException">Thrown if the TAP device is not open or the stream is unusable.</exception>
        /// <exception cref="IOException">An I/O error occurred during the read operation.</exception>
        /// <exception cref="ObjectDisposedException">The stream or TAP device has been disposed.</exception>
        /// <exception cref="OperationCanceledException">The read operation was canceled.</exception>
        public Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token)
        {
            if (!IsOpen)
            {
                _logMessageAction?.Invoke("ReadAsync: TAP device is not open or stream is unavailable.");
                throw new InvalidOperationException("TAP device is not open or stream is unavailable.");
            }
            // Logging every read can be very verbose, enable if debugging packet flow.
            // _logMessageAction?.Invoke($"TapDevice.ReadAsync: Attempting to read up to {count} bytes.");
            return _tapStream.ReadAsync(buffer, offset, count, token);
        }

        /// <summary>
        /// Asynchronously writes an IP packet to the TAP device.
        /// </summary>
        /// <param name="buffer">The buffer that contains the packet data to write to the stream.</param>
        /// <param name="offset">The zero-based byte offset in buffer at which to begin copying bytes to the current stream.</param>
        /// <param name="count">The number of bytes (packet size) to write.</param>
        /// <param name="token">The token to monitor for cancellation requests.</param>
        /// <returns>A task that represents the asynchronous write operation.</returns>
        /// <exception cref="InvalidOperationException">Thrown if the TAP device is not open or the stream is unusable.</exception>
        /// <exception cref="IOException">An I/O error occurred during the write operation.</exception>
        /// <exception cref="ObjectDisposedException">The stream or TAP device has been disposed.</exception>
        /// <exception cref="OperationCanceledException">The write operation was canceled.</exception>
        public Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken token)
        {
            if (!IsOpen)
            {
                _logMessageAction?.Invoke("WriteAsync: TAP device is not open or stream is unavailable.");
                throw new InvalidOperationException("TAP device is not open or stream is unavailable.");
            }
            // Logging every write can be very verbose.
            // _logMessageAction?.Invoke($"TapDevice.WriteAsync: Writing {count} bytes.");
            return _tapStream.WriteAsync(buffer, offset, count, token);
        }

        /// <summary>
        /// Closes the TAP device, sets its media status to disconnected, and releases all associated resources.
        /// </summary>
        public void Close()
        {
            _logMessageAction?.Invoke("TapDevice.Close() called.");
            // Check if the handle was ever valid and is not already closed before trying to set media status.
            if (_tapHandle != null && !_tapHandle.IsInvalid && !_tapHandle.IsClosed)
            {
                _logMessageAction?.Invoke("Close: Attempting to set TAP media status to 'Disconnected'.");
                SetMediaStatus(false); // Attempt to set media status, result is logged by SetMediaStatus.
            }
            else
            {
                _logMessageAction?.Invoke("Close: TAP handle was not valid or already closed; skipping SetMediaStatus(false).");
            }
            CloseHandleAndStream(); // This handles disposal of _tapStream and _tapHandle.
            _logMessageAction?.Invoke("TAP device closed and resources released.");
        }

        private bool _disposed = false; // To detect redundant calls

        /// <summary>
        /// Releases all resources used by the <see cref="TapDevice"/>.
        /// </summary>
        public void Dispose()
        {
            Dispose(true); // Dispose managed and unmanaged resources
            GC.SuppressFinalize(this); // Suppress finalization as cleanup is done
        }

        /// <summary>
        /// Protected method to dispose of resources.
        /// </summary>
        /// <param name="disposing">True if called from Dispose(), false if called from the finalizer.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

            // _logMessageAction?.Invoke($"TapDevice.Dispose(disposing={disposing}) called."); // Can be noisy
            if (disposing)
            {
                // Dispose managed state (managed objects).
                Close(); // Calls SetMediaStatus(false) and CloseHandleAndStream()
            }

            // Free unmanaged resources (unmanaged objects) if any were directly held.
            // SafeFileHandle takes care of the native handle, so direct cleanup here is usually not needed
            // unless other raw IntPtrs or unmanaged resources were allocated by this class directly.

            _disposed = true;
        }

        /// <summary>
        /// Finalizer for the TapDevice.
        /// </summary>
        /// <remarks>
        /// This finalizer is a safeguard. Proper use of the IDisposable pattern (e.g., via a using statement
        /// or explicit call to Dispose) is preferred to ensure timely cleanup of resources.
        /// </remarks>
        ~TapDevice()
        {
            // _logMessageAction?.Invoke("TapDevice finalizer ~TapDevice() called. This may indicate Dispose() was not called explicitly."); // Can be noisy
            Dispose(false); // Dispose only unmanaged resources (if any) from finalizer
        }
    }
}
