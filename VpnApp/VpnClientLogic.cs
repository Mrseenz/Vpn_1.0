using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace VpnApp // Added namespace
{
    public class VpnClientLogic : IDisposable // Implement IDisposable if it owns TapDevice
    {
        private UdpClient _udpClient;
        private IPEndPoint _proxyEndPoint;
        private TapDevice _tapDevice;
        private byte[] _encryptionKey;
        private CancellationTokenSource _cancellationTokenSource;
        private Task _tapListenerTask;
        private Task _socketListenerTask;
        private bool _isRunning = false;

        // Buffer for reading from TAP. Re-used to avoid frequent allocations.
        // Size should be large enough for typical MTU + headers.
        private readonly byte[] _tapReadBuffer = new byte[NativeMethods.DefaultPacketBufferSize]; // Using DefaultPacketBufferSize from NativeMethods or define here

        public event Action<string> LogMessage;

        public VpnClientLogic(string tapGuid, string proxyIp, int proxyPort, byte[] encryptionKey)
        {
            // TapDevice constructor now takes the GUID and logger
            _tapDevice = new TapDevice(tapGuid, (msg) => LogMessage?.Invoke($"[TAP] {msg}"));
            _proxyEndPoint = new IPEndPoint(IPAddress.Parse(proxyIp), proxyPort);
            _encryptionKey = encryptionKey;
        }

        public bool StartVpn()
        {
            if (_isRunning)
            {
                LogMessage?.Invoke("VPN Client Error: Already running.");
                return false;
            }

            LogMessage?.Invoke("VPN Client: Attempting to start...");
            _cancellationTokenSource = new CancellationTokenSource();

            // 1. Open the TAP device
            LogMessage?.Invoke("VPN Client: Opening TAP device...");
            if (!_tapDevice.Open()) // This now calls the real Open()
            {
                LogMessage?.Invoke("VPN Client Error: Failed to open TAP device. Check logs from TAP device itself. Ensure Admin rights and correct GUID.");
                _cancellationTokenSource.Dispose();
                _cancellationTokenSource = null;
                return false;
            }
            LogMessage?.Invoke("VPN Client: TAP device opened successfully.");

            try
            {
                // 2. Initialize UDP client
                // Using 0 for port tells OS to pick an available ephemeral port.
                _udpClient = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
                LogMessage?.Invoke($"VPN Client: UDP client bound to local endpoint {_udpClient.Client.LocalEndPoint}. Target proxy: {_proxyEndPoint}");
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke($"VPN Client Error: Failed to initialize UDP client: {ex.Message}");
                _tapDevice.Close(); // Close the TAP device if UDP setup fails
                _cancellationTokenSource.Dispose();
                _cancellationTokenSource = null;
                return false;
            }

            // 3. Start listener tasks
            _tapListenerTask = Task.Run(() => ReadFromTapAndSendAsync(_cancellationTokenSource.Token), _cancellationTokenSource.Token);
            _socketListenerTask = Task.Run(() => ReadFromSocketAndWriteAsync(_cancellationTokenSource.Token), _cancellationTokenSource.Token);

            _isRunning = true;
            LogMessage?.Invoke("VPN Client: Started successfully and listeners are running.");
            return true;
        }

        public async Task StopVpnAsync() // Made async to await listener tasks
        {
            if (!_isRunning)
            {
                LogMessage?.Invoke("VPN Client: Not running or already stopping.");
                return;
            }

            LogMessage?.Invoke("VPN Client: Attempting to stop...");
            if (_cancellationTokenSource != null && !_cancellationTokenSource.IsCancellationRequested)
            {
                _cancellationTokenSource.Cancel(); // Signal cancellation to tasks
            }

            // Close UDP client first, this will cause ReceiveAsync to throw and exit loop
            _udpClient?.Close();
            _udpClient?.Dispose();
            _udpClient = null;
            LogMessage?.Invoke("VPN Client: UDP client closed.");

            // Wait for listener tasks to complete (with a timeout)
            if (_tapListenerTask != null)
            {
                bool completed = await Task.WhenAny(_tapListenerTask, Task.Delay(TimeSpan.FromSeconds(5))) == _tapListenerTask;
                if (!completed) LogMessage?.Invoke("VPN Client Warning: TAP listener task did not complete within timeout.");
                else LogMessage?.Invoke("VPN Client: TAP listener task completed.");
            }
            if (_socketListenerTask != null)
            {
                 bool completed = await Task.WhenAny(_socketListenerTask, Task.Delay(TimeSpan.FromSeconds(5))) == _socketListenerTask;
                if (!completed) LogMessage?.Invoke("VPN Client Warning: Socket listener task did not complete within timeout.");
                 else LogMessage?.Invoke("VPN Client: Socket listener task completed.");
            }

            _tapDevice?.Dispose(); // This calls TapDevice.Close() which handles SetMediaStatus(false)
            LogMessage?.Invoke("VPN Client: TAP device disposed.");

            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;

            _isRunning = false;
            LogMessage?.Invoke("VPN Client: Stopped.");
        }

        private async Task ReadFromTapAndSendAsync(CancellationToken token)
        {
            LogMessage?.Invoke("VPN Client: TAP listener task started.");
            try
            {
                while (!token.IsCancellationRequested)
                {
                    // ReadAsync now takes the buffer and returns bytes read
                    int bytesRead = 0;
                    try
                    {
                        // Use the pre-allocated buffer _tapReadBuffer
                        bytesRead = await _tapDevice.ReadAsync(_tapReadBuffer, 0, _tapReadBuffer.Length, token);
                    }
                    catch (OperationCanceledException)
                    {
                        LogMessage?.Invoke("VPN Client: ReadFromTapAndSendAsync - ReadAsync cancelled. Exiting loop.");
                        break;
                    }
                    catch (IOException ex) // Includes ObjectDisposedException if stream is closed
                    {
                        LogMessage?.Invoke($"VPN Client: IOException during TAP read: {ex.Message}. Assuming TAP device closed. Exiting loop.");
                        break;
                    }
                    catch (InvalidOperationException ex) // If TAP device IsOpen becomes false
                    {
                        LogMessage?.Invoke($"VPN Client: InvalidOperationException during TAP read: {ex.Message}. Exiting loop.");
                        break;
                    }


                    if (bytesRead > 0)
                    {
                        LogMessage?.Invoke($"VPN Client: Read {bytesRead} bytes from TAP.");

                        // Create a new byte array for the packet with the exact size
                        byte[] actualPacket = new byte[bytesRead];
                        Buffer.BlockCopy(_tapReadBuffer, 0, actualPacket, 0, bytesRead);

                        string encryptedDataString = CryptographyHelper.Encrypt(actualPacket, _encryptionKey);
                        byte[] dataToSend = Encoding.UTF8.GetBytes(encryptedDataString);

                        if (_udpClient != null && _udpClient.Client != null && _udpClient.Client.IsBound) // Check if UdpClient is still valid
                        {
                            await _udpClient.SendAsync(dataToSend, dataToSend.Length, _proxyEndPoint);
                            // LogMessage?.Invoke($"VPN Client: Sent {dataToSend.Length} encrypted bytes to proxy {_proxyEndPoint}."); // Can be noisy
                        }
                        else
                        {
                            LogMessage?.Invoke("VPN Client: ReadFromTapAndSendAsync - UDP client is not available. Cannot send packet.");
                            break; // Exit if UDP client is gone
                        }
                    }
                    else if (token.IsCancellationRequested)
                    {
                        LogMessage?.Invoke("VPN Client: ReadFromTapAndSendAsync - Cancellation requested after read attempt. Exiting loop.");
                        break;
                    }
                    // If bytesRead is 0 and not cancelled, it might mean TAP driver has nothing to send at the moment. Loop continues.
                }
            }
            catch (Exception ex) // Catch-all for unexpected errors in the loop
            {
                if (!token.IsCancellationRequested) // Don't log if it's a known cancellation path
                {
                    LogMessage?.Invoke($"VPN Client Error: Unexpected error in ReadFromTapAndSendAsync: {ex.ToString()}");
                }
            }
            finally
            {
                LogMessage?.Invoke("VPN Client: TAP listener task finished.");
            }
        }

        private async Task ReadFromSocketAndWriteAsync(CancellationToken token)
        {
            LogMessage?.Invoke("VPN Client: Socket listener task started.");
            try
            {
                while (!token.IsCancellationRequested)
                {
                    UdpReceiveResult result;
                    try
                    {
                        if (_udpClient == null || _udpClient.Client == null || !_udpClient.Client.IsBound)
                        {
                            LogMessage?.Invoke("VPN Client: ReadFromSocketAndWriteAsync - UDP client is not available. Exiting loop.");
                            break;
                        }
                        // UdpClient.ReceiveAsync doesn't directly accept a CancellationToken.
                        // It's typically cancelled by closing the UdpClient from another thread (_udpClient.Close()).
                        result = await _udpClient.ReceiveAsync();
                    }
                    catch (SocketException ex) when (ex.SocketErrorCode == SocketError.Interrupted || ex.SocketErrorCode == SocketError.OperationAborted || token.IsCancellationRequested)
                    {
                        LogMessage?.Invoke("VPN Client: ReadFromSocketAndWriteAsync - UDP ReceiveAsync cancelled or socket closed. Exiting loop.");
                        break;
                    }
                    catch (ObjectDisposedException) // UdpClient was disposed
                    {
                        LogMessage?.Invoke("VPN Client: ReadFromSocketAndWriteAsync - UdpClient disposed. Exiting loop.");
                        break;
                    }
                    // Other SocketExceptions could be real network errors.

                    if (token.IsCancellationRequested) // Check token after await, in case cancelled while waiting
                    {
                        LogMessage?.Invoke("VPN Client: ReadFromSocketAndWriteAsync - Cancellation requested. Exiting loop.");
                        break;
                    }

                    byte[] receivedData = result.Buffer;
                    // LogMessage?.Invoke($"VPN Client: Received {receivedData.Length} bytes from proxy {result.RemoteEndPoint}."); // Can be noisy

                    string encryptedString = Encoding.UTF8.GetString(receivedData);
                    byte[] decryptedPacket = CryptographyHelper.Decrypt(encryptedString, _encryptionKey);

                    if (decryptedPacket != null && decryptedPacket.Length > 0)
                    {
                        try
                        {
                            await _tapDevice.WriteAsync(decryptedPacket, 0, decryptedPacket.Length, token);
                            // LogMessage?.Invoke($"VPN Client: Wrote {decryptedPacket.Length} decrypted bytes to TAP."); // Can be noisy
                        }
                        catch (OperationCanceledException)
                        {
                            LogMessage?.Invoke("VPN Client: ReadFromSocketAndWriteAsync - WriteAsync to TAP cancelled. Exiting loop.");
                            break;
                        }
                        catch (IOException ex) // Includes ObjectDisposedException if stream is closed
                        {
                            LogMessage?.Invoke($"VPN Client: IOException during TAP write: {ex.Message}. Assuming TAP device closed. Exiting loop.");
                            break;
                        }
                        catch (InvalidOperationException ex) // If TAP device IsOpen becomes false
                        {
                            LogMessage?.Invoke($"VPN Client: InvalidOperationException during TAP write: {ex.Message}. Exiting loop.");
                            break;
                        }
                    }
                }
            }
            catch (Exception ex) // Catch-all for unexpected errors in the loop
            {
                 if (!token.IsCancellationRequested)
                {
                    LogMessage?.Invoke($"VPN Client Error: Unexpected error in ReadFromSocketAndWriteAsync: {ex.ToString()}");
                }
            }
            finally
            {
                LogMessage?.Invoke("VPN Client: Socket listener task finished.");
            }
        }

        // Implement IDisposable if VpnClientLogic owns disposable resources like TapDevice
        private bool _disposedValue;
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    // Dispose managed state (managed objects).
                    LogMessage?.Invoke("VPN Client: Disposing VpnClientLogic...");
                    if (_isRunning)
                    {
                        // This might be called from UI thread, StopVpnAsync is better
                        // but for synchronous dispose, we might need to force it.
                        // For now, assume StopVpnAsync is called before Dispose by the owner.
                        // If not, a synchronous stop might be needed here, which is complex.
                        LogMessage?.Invoke("VPN Client Warning: Dispose called while VPN might be running. Call StopVpnAsync first.");
                    }
                    _cancellationTokenSource?.Dispose();
                    _udpClient?.Dispose(); // Already disposed in StopVpnAsync usually
                    _tapDevice?.Dispose(); // Ensure TapDevice is disposed
                }
                _disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
