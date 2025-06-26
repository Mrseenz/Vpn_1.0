using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles; // For SafeFileHandle

namespace VpnApp
{
    internal static class NativeMethods
    {
        #region Kernel32.dll Imports

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        internal static extern SafeFileHandle CreateFile(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes, // Optional SECURITY_ATTRIBUTES structure
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true, CharSet = CharSet.Auto)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool DeviceIoControl(
            SafeFileHandle hDevice,
            uint dwIoControlCode,
            IntPtr lpInBuffer,
            uint nInBufferSize,
            IntPtr lpOutBuffer, // Using IntPtr for flexibility, can be specific struct
            uint nOutBufferSize,
            out uint lpBytesReturned,
            IntPtr lpOverlapped); // IntPtr for OVERLAPPED structure, often null for synchronous

        // Overload for DeviceIoControl with specific output structure (example for Get MAC)
        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true, CharSet = CharSet.Auto)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool DeviceIoControl(
            SafeFileHandle hDevice,
            uint dwIoControlCode,
            IntPtr lpInBuffer,
            uint nInBufferSize,
            [Out] byte[] lpOutBuffer, // Specifically for byte array output like MAC address
            uint nOutBufferSize,
            out uint lpBytesReturned,
            IntPtr lpOverlapped);


        #endregion

        #region CreateFile Constants

        internal const uint GENERIC_READ = 0x80000000;
        internal const uint GENERIC_WRITE = 0x40000000;
        internal const uint FILE_SHARE_READ = 0x00000001;
        internal const uint FILE_SHARE_WRITE = 0x00000002;
        internal const uint OPEN_EXISTING = 3;
        internal const uint FILE_ATTRIBUTE_SYSTEM = 0x00000004;
        internal const uint FILE_FLAG_OVERLAPPED = 0x40000000; // Important for async I/O with FileStream

        #endregion

        #region SetupAPI.dll Imports & Structures

        [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
        internal static extern IntPtr SetupDiGetClassDevs(
            ref Guid ClassGuid,
            IntPtr Enumerator, // Typically null or specific class
            IntPtr hwndParent, // Typically null
            uint Flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetupDiEnumDeviceInterfaces(
            IntPtr hDevInfo,
            IntPtr devInfo, // Optional SP_DEVINFO_DATA
            ref Guid interfaceClassGuid,
            uint memberIndex,
            ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);

        [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetupDiGetDeviceInterfaceDetail(
            IntPtr hDevInfo,
            ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData,
            IntPtr deviceInterfaceDetailData, // Will be allocated SP_DEVICE_INTERFACE_DETAIL_DATA_A/W
            uint deviceInterfaceDetailDataSize,
            out uint requiredSize,
            IntPtr deviceInfoData); // Optional SP_DEVINFO_DATA

        // Using ANSI version for simplicity with path char array
        [DllImport("setupapi.dll", CharSet = CharSet.Ansi, SetLastError = true)] // CharSet.Ansi
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetupDiGetDeviceInterfaceDetailA(
            IntPtr hDevInfo,
            ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData,
            ref SP_DEVICE_INTERFACE_DETAIL_DATA_A deviceInterfaceDetailData, // Note: Changed to A
            uint deviceInterfaceDetailDataSize,
            out uint requiredSize,
            IntPtr deviceInfoData); // Optional SP_DEVINFO_DATA

        [DllImport("setupapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetupDiDestroyDeviceInfoList(IntPtr hDevInfo);

        [StructLayout(LayoutKind.Sequential)]
        internal struct SP_DEVINFO_DATA
        {
            public uint cbSize;
            public Guid ClassGuid;
            public uint DevInst;
            public IntPtr Reserved; // Or UIntPtr
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct SP_DEVICE_INTERFACE_DATA
        {
            public uint cbSize;
            public Guid InterfaceClassGuid;
            public uint Flags;
            public IntPtr Reserved; // Or UIntPtr
        }

        // Using ANSI version of detail data structure
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)] // CharSet.Ansi
        internal struct SP_DEVICE_INTERFACE_DETAIL_DATA_A
        {
            public uint cbSize;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] // Fixed size string for device path
            public string DevicePath;
        }
        // If using Unicode (SetupDiGetDeviceInterfaceDetailW), use CharSet.Unicode and adjust string marshalling

        internal const uint DIGCF_PRESENT = 0x00000002;
        internal const uint DIGCF_DEVICEINTERFACE = 0x00000010;

        // GUID for Network Adapters (commonly used, though TAP might have its own specific one)
        // Standard Network Adapter Class GUID
        internal static readonly Guid GUID_DEVCLASS_NET = new Guid("{4d36e972-e325-11ce-bfc1-08002be10318}");
        // GUID for TAP-Windows Adapter (example, may vary by specific driver version/type)
        // This is an example GUID for OpenVPN_TAP_WIN_COMPONENT_ID "tap0901"
        // For modern tap-windows6, it might be different or you might need to find it by friendly name or other properties.
        // It's often better to iterate all network interfaces and check properties if a specific TAP class GUID isn't known/reliable.
        internal static readonly Guid GUID_TAP_WINDOWS_PROVIDER = new Guid("{YOUR-TAP-PROVIDER-SPECIFIC-INTERFACE-CLASS-GUID}"); // Placeholder!

        internal const int INVALID_HANDLE_VALUE_INT = -1; // For SetupDiGetClassDevs return

        #endregion

        #region TAP IOCTL Codes
        // These are typical for tap0901. Modern drivers (tap-windows6) might have different codes or methods.
        // The IOCTL codes are defined using the CTL_CODE macro in C:
        // #define CTL_CODE( DeviceType, Function, Method, Access ) ( \
        // ((DeviceType) << 16) | ((Access) << 14) | ((Function) << 2) | (Method) \
        // )
        // For TAP drivers, DeviceType is often FILE_DEVICE_UNKNOWN (0x22)

        private const uint FILE_DEVICE_UNKNOWN = 0x00000022;
        private const uint METHOD_BUFFERED = 0;
        // private const uint FILE_ANY_ACCESS = 0; // Not used in this macro directly
        // private const uint FILE_READ_DATA = 0x0001;
        // private const uint FILE_WRITE_DATA = 0x0002;

        private static uint TAP_CONTROL_CODE(uint request, uint method)
        {
            // For TAP drivers, DeviceType is typically FILE_DEVICE_UNKNOWN (0x00000022)
            // Access is usually FILE_ANY_ACCESS (0) for many common IOCTLs, or specific for others.
            // The Function codes (request) are specific to the TAP driver (e.g., 1 to 10 for tap0901)
            // The Method is usually METHOD_BUFFERED (0)
            // ((DeviceType) << 16) | ((Access) << 14) | ((Function) << 2) | (Method)
            // For simplicity, assuming FILE_ANY_ACCESS for Access part of CTL_CODE
            return (FILE_DEVICE_UNKNOWN << 16) | (0u << 14) | (request << 2) | method;
        }

        // Get MAC address
        // #define TAP_IOCTL_GET_MAC               TAP_CONTROL_CODE (1, METHOD_BUFFERED)
        internal static readonly uint TAP_IOCTL_GET_MAC = TAP_CONTROL_CODE(1, METHOD_BUFFERED); // Output: 6 bytes

        // Get driver version
        // #define TAP_IOCTL_GET_VERSION           TAP_CONTROL_CODE (2, METHOD_BUFFERED)
        internal static readonly uint TAP_IOCTL_GET_VERSION = TAP_CONTROL_CODE(2, METHOD_BUFFERED); // Output: array of 3 int

        // Get MTU
        // #define TAP_IOCTL_GET_MTU               TAP_CONTROL_CODE (3, METHOD_BUFFERED)
        internal static readonly uint TAP_IOCTL_GET_MTU = TAP_CONTROL_CODE(3, METHOD_BUFFERED); // Output: int

        // Get info (debug)
        // #define TAP_IOCTL_GET_INFO              TAP_CONTROL_CODE (4, METHOD_BUFFERED)

        // Set media status (connected/disconnected)
        // #define TAP_IOCTL_SET_MEDIA_STATUS      TAP_CONTROL_CODE (6, METHOD_BUFFERED) // Input: int (0=disconnect, 1=connect)
        internal static readonly uint TAP_IOCTL_SET_MEDIA_STATUS = TAP_CONTROL_CODE(6, METHOD_BUFFERED);

        // Configure TUN interface parameters (IP address, network, and mask of the virtual TUN interface)
        // #define TAP_IOCTL_CONFIG_TUN            TAP_CONTROL_CODE (10, METHOD_BUFFERED)
        // Input: array of 3 IP addresses (local_tun_ip, remote_tun_net_ip, local_tun_mask)
        // This is complex and often done via netsh instead for more control.
        internal static readonly uint TAP_IOCTL_CONFIG_TUN = TAP_CONTROL_CODE(10, METHOD_BUFFERED);

        // Other IOCTLs like CONFIG_POINT_TO_POINT, CONFIG_DHCP, GET_LOG_LINE exist for tap0901
        // but are not included here for brevity. Modern tap-windows6 might use NDIS OIDs
        // or different IOCTLs entirely for some of these operations.

        #endregion

        #region Error Codes (Common Win32 error codes)
        internal const int ERROR_SUCCESS = 0;
        internal const int ERROR_FILE_NOT_FOUND = 2;
        internal const int ERROR_PATH_NOT_FOUND = 3;
        internal const int ERROR_ACCESS_DENIED = 5;
        internal const int ERROR_INVALID_HANDLE = 6;
        internal const int ERROR_INSUFFICIENT_BUFFER = 122;
        internal const int ERROR_NO_MORE_ITEMS = 259;
        internal const int ERROR_OPERATION_ABORTED = 995; // Often due to thread exiting or I/O cancelled
        internal const int ERROR_IO_PENDING = 997; // For overlapped I/O
        #endregion

        #region SetupAPI Device Registry Properties (SPDRP codes)
        internal const uint SPDRP_DEVICEDESC = 0x00000000;  // DeviceDesc (R/W)
        internal const uint SPDRP_HARDWAREID = 0x00000001;  // HardwareID (R/W) - Multi-string
        internal const uint SPDRP_COMPATIBLEIDS = 0x00000002; // CompatibleIDs (R/W) - Multi-string
        internal const uint SPDRP_UNUSED0 = 0x00000003;  // Unused
        internal const uint SPDRP_SERVICE = 0x00000004;  // Service (R/W)
        internal const uint SPDRP_UNUSED1 = 0x00000005;  // Unused
        internal const uint SPDRP_UNUSED2 = 0x00000006;  // Unused
        internal const uint SPDRP_CLASS = 0x00000007;  // Class (R--tied to ClassGUID)
        internal const uint SPDRP_CLASSGUID = 0x00000008;  // ClassGUID (R)
        internal const uint SPDRP_DRIVER = 0x00000009;  // Driver (R/W)
        internal const uint SPDRP_CONFIGFLAGS = 0x0000000A;  // ConfigFlags (R/W)
        internal const uint SPDRP_MFG = 0x0000000B;  // Manufacturer (R/W)
        internal const uint SPDRP_FRIENDLYNAME = 0x0000000C;  // FriendlyName (R/W)
        internal const uint SPDRP_LOCATION_INFORMATION = 0x0000000D;  // LocationInformation (R/W)
        internal const uint SPDRP_PHYSICAL_DEVICE_OBJECT_NAME = 0x0000000E;  // PhysicalDeviceObjectName (R)
        internal const uint SPDRP_CAPABILITIES = 0x0000000F;  // Capabilities (R)
        internal const uint SPDRP_UI_NUMBER = 0x00000010;  // UiNumber (R)
        internal const uint SPDRP_UPPERFILTERS = 0x00000011;  // UpperFilters (R/W) - Multi-string
        internal const uint SPDRP_LOWERFILTERS = 0x00000012;  // LowerFilters (R/W) - Multi-string
        internal const uint SPDRP_BUSTYPEGUID = 0x00000013;  // BusTypeGUID (R)
        internal const uint SPDRP_LEGACYBUSTYPE = 0x00000014;  // LegacyBusType (R)
        internal const uint SPDRP_BUSNUMBER = 0x00000015;  // BusNumber (R)
        // This is the {GUID} string that identifies the network interface instance for NetCfg.
        // This is what should match the GUID the user provides for the TAP adapter.
        internal const uint SPDRP_NETCFGINSTANCEID = 0x00000016; // NetCfgInstanceID (R)
        internal const uint SPDRP_CHARACTERISTICS = 0x0000001B;  // Characteristics (R)
        internal const uint SPDRP_ADDRESS = 0x0000001C;  // Device Address (R)
        internal const uint SPDRP_UI_NUMBER_DESC_FORMAT = 0x0000001D; // UiNumberDescFormat (R/W)
        // Add other SPDRP_ constants if needed for more detailed device identification.
        #endregion

        #region Additional SetupAPI Imports for Device Properties

        // Definition for SetupDiGetDeviceRegistryProperty
        // Using CharSet.Auto for flexibility, though property strings are often Unicode.
        // The buffer should be sized appropriately and string conversion handled carefully.
        [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetupDiGetDeviceRegistryProperty(
            IntPtr deviceInfoSet,                       // Handle to device information set
            ref SP_DEVINFO_DATA deviceInfoData,         // Pointer to SP_DEVINFO_DATA structure
            uint property,                              // Property to retrieve (SPDRP_*)
            out uint propertyRegDataType,               // Output: Registry data type (REG_SZ, REG_MULTI_SZ, etc.)
            [Out] byte[] propertyBuffer,                // Buffer to receive the property data
            uint propertyBufferSize,                    // Size of propertyBuffer
            out uint requiredSize);                     // Output: Required size of the buffer
        #endregion
    }
}
```

**Key points about this `NativeMethods.cs`:**
*   It uses `SafeFileHandle` for `CreateFile` which is good practice for managing native handles.
*   `DeviceIoControl` has two overloads: one generic with `IntPtr` for buffers, and one specific for `byte[]` output (useful for `TAP_IOCTL_GET_MAC`).
*   SetupAPI structures and functions are included for device enumeration. I've used the `A` (ANSI) versions of `SetupDiGetDeviceInterfaceDetail` and its structure for simplicity in marshalling the `DevicePath`. If strict Unicode path handling is needed, the `W` versions would be used with `CharSet.Unicode`.
*   **`GUID_TAP_WINDOWS_PROVIDER` is a critical placeholder.** Finding the correct interface class GUID for the specific TAP driver in use, or iterating all network interfaces and identifying the TAP adapter by other means (e.g., "ComponentId" like `tap0901` or friendly name), is essential for device discovery. I've included `GUID_DEVCLASS_NET` which is the general network adapter class.
*   The TAP IOCTL codes are based on older `tap0901` driver common knowledge. **These are highly likely to need verification or adjustment for modern `tap-windows6` drivers.** Modern drivers might rely more on NDIS OID requests or different IOCTL codes.
*   `TAP_IOCTL_CONFIG_TUN` is included but its usage is complex and often superseded by using `netsh` or other OS networking APIs to configure the IP address on the TAP interface *after* it's opened and connected.

This forms the basis for the P/Invoke layer. The next step will be to use these in the `TapDevice.cs` implementation, particularly for device discovery.
