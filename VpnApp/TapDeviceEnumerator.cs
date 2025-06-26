using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Text; // For Encoding used in GetDeviceRegistryProperty

namespace VpnApp
{
    /// <summary>
    /// Provides functionality to enumerate TAP-Windows adapters installed on the system.
    /// It uses Windows SetupAPI to find devices and their properties.
    /// This class helps in translating a user-friendly TAP Instance GUID (NetCfgInstanceID)
    /// into a system device path required for CreateFile.
    /// </summary>
    public static class TapDeviceEnumerator
    {
        private static Action<string> _logger = Console.WriteLine;

        /// <summary>
        /// Initializes the logger for the enumerator. Should be called once.
        /// </summary>
        /// <param name="logger">Action to call for logging messages. If null, defaults to Console.WriteLine.</param>
        public static void InitializeLogger(Action<string> logger)
        {
            _logger = logger ?? Console.WriteLine;
        }

        /// <summary>
        /// Holds information about a discovered TAP device.
        /// </summary>
        public struct TapDeviceInfo
        {
            /// <summary>
            /// The system device path for the TAP adapter, usable with CreateFile.
            /// Example: \\?\root#net#0000#{...} (Symbolic link to the device)
            /// </summary>
            public string DevicePath { get; set; }
            /// <summary>
            /// The network configuration instance GUID (NetCfgInstanceID) of the TAP adapter.
            /// This is typically the {GUID} string that users might configure.
            /// </summary>
            public string InstanceGuid { get; set; }
            /// <summary>
            /// The component ID of the TAP driver (e.g., "tap0901", "tap_windows6").
            /// Extracted from HardwareID.
            /// </summary>
            public string ComponentId { get; set; }
            /// <summary>
            /// The driver description for the TAP adapter (e.g., "TAP-Windows Adapter V9").
            /// </summary>
            public string Description { get; set; }
        }

        /// <summary>
        /// Helper to get specific device registry properties as strings.
        /// </summary>
        private static string GetDeviceRegistryProperty(IntPtr deviceInfoSet, ref NativeMethods.SP_DEVINFO_DATA deviceInfoData, uint property)
        {
            uint propertyRegDataType = 0;
            uint requiredSize = 0;
            // Start with a reasonably sized buffer. If it's a multi-sz string, it might need to be larger or handled iteratively.
            byte[] propertyBuffer = new byte[1024];

            // First call to get required size (optional, some properties might not return correct size initially)
            // NativeMethods.SetupDiGetDeviceRegistryProperty(deviceInfoSet, ref deviceInfoData, property, out _, null, 0, out requiredSize);
            // if (requiredSize > propertyBuffer.Length) propertyBuffer = new byte[requiredSize];

            if (NativeMethods.SetupDiGetDeviceRegistryProperty(deviceInfoSet, ref deviceInfoData, property, out propertyRegDataType, propertyBuffer, (uint)propertyBuffer.Length, out requiredSize))
            {
                // PropertyRegDataType: 1 = REG_SZ, 7 = REG_MULTI_SZ
                if (propertyRegDataType == 1 || propertyRegDataType == 7)
                {
                    // For REG_SZ, requiredSize includes the null terminator.
                    // For REG_MULTI_SZ, it includes two null terminators at the end of the list.
                    // Encoding.Unicode.GetString handles null terminators correctly if we specify exact length.
                    // Subtracting 2 for REG_SZ to remove the single null terminator for cleaner string.
                    // For REG_MULTI_SZ, this would give the first string if we subtract 2.
                    // If requiredSize is 0 or 2 (empty string), handle appropriately.
                    if (requiredSize > 0) {
                        // Trim trailing nulls for cleaner output, especially for single strings
                        int lengthToUse = (int)requiredSize;
                        while (lengthToUse > 0 && propertyBuffer[lengthToUse-1] == 0) {
                            lengthToUse--;
                        }
                         // Use Unicode as device properties are generally stored as such.
                        return Encoding.Unicode.GetString(propertyBuffer, 0, lengthToUse);
                    }
                    return string.Empty;
                }
                 _logger?.Invoke($"GetDeviceRegistryProperty: Property '{property}' has unexpected data type: {propertyRegDataType}");
            }
            else
            {
                int error = Marshal.GetLastWin32Error();
                // ERROR_INVALID_DATA can occur if property doesn't exist for the device.
                if (error != 0 && error != 122 /*ERROR_INSUFFICIENT_BUFFER if first call for size was not made or buffer too small*/) {
                    // _logger?.Invoke($"GetDeviceRegistryProperty failed for property code {property}: {new Win32Exception(error).Message}");
                }
            }
            return string.Empty;
        }

        /// <summary>
        /// Finds all available TAP-like network adapters on the system.
        /// It iterates through network class devices and checks their properties (HardwareID, Description)
        /// to identify potential TAP adapters.
        /// </summary>
        /// <returns>A list of <see cref="TapDeviceInfo"/> for each found TAP adapter.</returns>
        public static List<TapDeviceInfo> FindTapDevices()
        {
            var tapDevices = new List<TapDeviceInfo>();
            IntPtr deviceInfoSet = IntPtr.Zero;

            try
            {
                Guid netClassGuid = NativeMethods.GUID_DEVCLASS_NET;
                deviceInfoSet = NativeMethods.SetupDiGetClassDevs(ref netClassGuid, null, IntPtr.Zero, NativeMethods.DIGCF_PRESENT | NativeMethods.DIGCF_DEVICEINTERFACE);

                if (deviceInfoSet == NativeMethods.INVALID_HANDLE_VALUE)
                {
                    _logger($"SetupDiGetClassDevs failed: {new Win32Exception(Marshal.GetLastWin32Error()).Message}");
                    return tapDevices;
                }

                uint memberIndex = 0;
                var deviceInterfaceData = new NativeMethods.SP_DEVICE_INTERFACE_DATA();
                deviceInterfaceData.cbSize = (uint)Marshal.SizeOf(deviceInterfaceData);

                // SP_DEVINFO_DATA is required to get device instance properties (like HardwareID)
                var deviceInfoData = new NativeMethods.SP_DEVINFO_DATA();
                deviceInfoData.cbSize = (uint)Marshal.SizeOf(deviceInfoData);

                while (NativeMethods.SetupDiEnumDeviceInterfaces(deviceInfoSet, IntPtr.Zero, ref netClassGuid, memberIndex, ref deviceInterfaceData))
                {
                    memberIndex++;
                    uint requiredSize = 0;

                    var deviceInterfaceDetailData = new NativeMethods.SP_DEVICE_INTERFACE_DETAIL_DATA_A();
                    // For SP_DEVICE_INTERFACE_DETAIL_DATA_A, cbSize MUST be set correctly.
                    // It's the size of the fixed part of the structure, not the whole buffer.
                    // For ANSI, this is 4 (for cbSize uint field) + 1 (for the first char of DevicePath TCHAR array). So, 5.
                    // For Unicode (SP_DEVICE_INTERFACE_DETAIL_DATA_W), it would be 4 + 2 = 6.
                    // Using SystemDefaultCharSize helps make this more robust if code is switched to Unicode details later.
                    deviceInterfaceDetailData.cbSize = 4 + (uint)Marshal.SystemDefaultCharSize;

                    // First call to get the required size for the detail data structure itself (variable length part)
                    // Passing null for detailData buffer, and 0 for size.
                    NativeMethods.SetupDiGetDeviceInterfaceDetailA(deviceInfoSet, ref deviceInterfaceData, IntPtr.Zero, 0, out requiredSize, ref deviceInfoData); // Pass deviceInfoData here

                    if (requiredSize == 0 && Marshal.GetLastWin32Error() != NativeMethods.ERROR_INSUFFICIENT_BUFFER) {
                         _logger($"SetupDiGetDeviceInterfaceDetailA (size check) failed or returned size 0 unexpectedly: {new Win32Exception(Marshal.GetLastWin32Error()).Message}");
                        continue;
                    }
                    // If requiredSize is 0 but no error, it's odd, but proceed if no error.
                    // However, ERROR_INSUFFICIENT_BUFFER is expected here.

                    // Now call again with the correctly sized buffer (or use the ref struct if requiredSize matches its capacity)
                    // Our SP_DEVICE_INTERFACE_DETAIL_DATA_A has a fixed buffer for DevicePath.
                    // If requiredSize is larger than this fixed buffer, then this approach with fixed struct would truncate.
                    // However, device paths are usually within 256 chars. Max path is 260.
                    if (NativeMethods.SetupDiGetDeviceInterfaceDetailA(deviceInfoSet, ref deviceInterfaceData, ref deviceInterfaceDetailData, requiredSize, out _, ref deviceInfoData))
                    {
                        string currentDevicePath = deviceInterfaceDetailData.DevicePath;

                        // deviceInfoData is now populated because it was passed to SetupDiGetDeviceInterfaceDetailA.
                        // (It's associated with the hDevInfo and the specific interface being detailed).
                        string hardwareIdMultiSz = GetDeviceRegistryProperty(deviceInfoSet, ref deviceInfoData, NativeMethods.SPDRP_HARDWAREID);
                        string componentId = ExtractTapComponentId(hardwareIdMultiSz);
                        string netCfgInstanceID = GetDeviceRegistryProperty(deviceInfoSet, ref deviceInfoData, NativeMethods.SPDRP_NETCFGINSTANCEID);
                        string driverDesc = GetDeviceRegistryProperty(deviceInfoSet, ref deviceInfoData, NativeMethods.SPDRP_DEVICEDESC);

                        if (componentId.StartsWith("tap", StringComparison.OrdinalIgnoreCase) ||
                            (driverDesc != null && driverDesc.ToLowerInvariant().Contains("tap-windows")) ||
                            (hardwareIdMultiSz != null && hardwareIdMultiSz.ToLowerInvariant().Contains("tap")))
                        {
                            _logger($"Found TAP Adapter: Path='{currentDevicePath}', NetCfgInstanceID='{netCfgInstanceID}', ComponentID='{componentId}', Description='{driverDesc}'");
                            tapDevices.Add(new TapDeviceInfo {
                                DevicePath = currentDevicePath,
                                InstanceGuid = netCfgInstanceID,
                                ComponentId = componentId,
                                Description = driverDesc
                            });
                        }
                    }
                    else
                    {
                        _logger($"SetupDiGetDeviceInterfaceDetailA (data get) failed for index {memberIndex-1}: {new Win32Exception(Marshal.GetLastWin32Error()).Message}");
                    }
                }

                int lastError = Marshal.GetLastWin32Error();
                if (memberIndex == 0 && lastError != NativeMethods.ERROR_NO_MORE_ITEMS) {
                     _logger($"No network interfaces found. SetupDiEnumDeviceInterfaces error: {new Win32Exception(lastError).Message}");
                } else if (lastError != 0 && lastError != NativeMethods.ERROR_NO_MORE_ITEMS) {
                    _logger($"SetupDiEnumDeviceInterfaces loop error: {new Win32Exception(lastError).Message}");
                }
            }
            catch (Exception ex)
            {
                _logger($"Exception in FindTapDevices: {ex.ToString()}");
            }
            finally
            {
                if (deviceInfoSet != IntPtr.Zero && deviceInfoSet != NativeMethods.INVALID_HANDLE_VALUE)
                {
                    NativeMethods.SetupDiDestroyDeviceInfoList(deviceInfoSet);
                }
            }
            return tapDevices;
        }

        /// <summary>
        /// Extracts a TAP component ID (like "tap0901" or "tap_windows6") from the hardware ID multi-string.
        /// </summary>
        private static string ExtractTapComponentId(string hardwareIdMultiSz)
        {
            if (string.IsNullOrEmpty(hardwareIdMultiSz)) return "unknown";

            // Hardware IDs can be a multi-string (list of null-terminated strings, ending with a double null).
            // We check each ID in the list.
            string[] ids = hardwareIdMultiSz.Split(new[] { '\0' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string id in ids)
            {
                // Common TAP component IDs:
                // tap0901 (older OpenVPN)
                // root\tap0901 (sometimes includes root)
                // tap_windows6 (newer OpenVPN)
                // PCI\VEN_1AF4&DEV_1000&SUBSYS_00011AF4&REV_00 (This is VirtIO NIC, not TAP, but shows format)
                // For TAP, it's often a "software" device like "root\tapxxxx"
                string lowerId = id.ToLowerInvariant();
                if (lowerId.Contains("tap0901")) return "tap0901";
                if (lowerId.Contains("tap_windows6")) return "tap_windows6"; // For newer drivers
                if (lowerId.Contains("tap")) return id; // Return the specific ID if "tap" is in it as a fallback
            }
            return "unknown"; // Or the first ID if no specific TAP pattern matches
        }

        /// <summary>
        /// Gets the system device path for a TAP adapter matching the given NetCfgInstanceID.
        /// </summary>
        /// <param name="instanceGuidToFind">The NetCfgInstanceID (e.g., "{xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx}") of the TAP adapter to find.</param>
        /// <returns>The device path string if found; otherwise, null.</returns>
        public static string GetDevicePathByInstanceGuid(string instanceGuidToFind)
        {
            if (string.IsNullOrWhiteSpace(instanceGuidToFind))
            {
                _logger("GetDevicePathByInstanceGuid: Provided instance GUID is null or empty.");
                return null;
            }

            // NetCfgInstanceID is typically stored as {GUID_STRING} in the registry.
            string normalizedGuidToFind = instanceGuidToFind.Trim();
            if (!normalizedGuidToFind.StartsWith("{", StringComparison.Ordinal)) normalizedGuidToFind = "{" + normalizedGuidToFind;
            if (!normalizedGuidToFind.EndsWith("}", StringComparison.Ordinal)) normalizedGuidToFind = normalizedGuidToFind + "}";

            _logger($"Searching for TAP device with NetCfgInstanceID: {normalizedGuidToFind}");
            var devices = FindTapDevices();

            if (devices.Count == 0) {
                _logger("No TAP devices found during enumeration by GetDevicePathByInstanceGuid.");
            }

            foreach (var devInfo in devices)
            {
                 //_logger($"Checking enumerated device: Path='{devInfo.DevicePath}', Found InstanceGUID='{devInfo.InstanceGuid}' vs Target='{normalizedGuidToFind}'");
                if (devInfo.InstanceGuid.Equals(normalizedGuidToFind, StringComparison.OrdinalIgnoreCase))
                {
                    _logger($"Match found for NetCfgInstanceID '{normalizedGuidToFind}': Path='{devInfo.DevicePath}'");
                    // The DevicePath from SetupDiGetDeviceInterfaceDetailA is directly usable by CreateFile.
                    // It's a symbolic link to the device. e.g. \\?\ROOT#NET#0000#{...}
                    return devInfo.DevicePath;
                }
            }
            _logger($"No TAP device found matching NetCfgInstanceID: {instanceGuidToFind}");
            return null;
        }
    }
}
