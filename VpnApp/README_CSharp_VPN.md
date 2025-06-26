```markdown
# Zero-Rated VPN Tool (C# WPF Version)

## 1. Overview

This application provides a VPN client and an optional local HTTP proxy. The VPN client is designed to capture traffic from a virtual TAP network interface, encrypt it, and send it to a configured UDP proxy server. The local HTTP proxy can forward HTTP traffic to a specified "zero-rated" domain.

**Disclaimer:** This tool is provided for educational purposes to understand VPN, networking, and encryption concepts. Misuse of this tool to bypass ISP terms of service or for any illegal activities is strongly discouraged. Users are responsible for complying with all local laws and ISP policies. The TAP device interaction and network routing require administrator privileges.

## 2. Features

*   VPN client to tunnel traffic via a TAP interface using a C# implementation for TAP device control.
*   Traffic encryption between the client and the remote UDP proxy.
*   Configurable remote UDP proxy IP and port.
*   Generates and uses a local encryption key (AES-based).
*   Optional local HTTP proxy to forward requests to a zero-rated domain.
*   GUI for configuration and operation.
*   Saves settings between sessions.
*   Logging of operations, including detailed TAP device interaction steps.

## 3. Prerequisites

*   **Windows Operating System:** (Developed for Windows, uses Windows-specific APIs).
*   **.NET Desktop Runtime:** Version 6.0 or newer (or as specified by the build). Download from the official Microsoft .NET website.
*   **TAP-Windows Adapter:** A virtual TAP network adapter must be installed and enabled.
    *   The official OpenVPN package (e.g., version 2.4.x, 2.5.x, or 2.6.x) is a common source for the `tap-windows6` driver.
    *   Download OpenVPN from [https://openvpn.net/community-downloads/](https://openvpn.net/community-downloads/).
    *   During installation, ensure the "TAP Virtual Ethernet Adapter" component is selected.
*   **Administrator Privileges:** The application **must** be run as an administrator to discover, open, and control the TAP device, and potentially to bind the HTTP proxy to all interfaces.

## 4. Setup and Usage

### a. Installation (if distributed as a zip)

1.  Extract the application files (e.g., `VpnApp.exe` and supporting DLLs) to a folder on your computer.
2.  Ensure all prerequisites (see section 3) are installed.

### b. Configuration

1.  **Run `VpnApp.exe` as Administrator.**
2.  **VPN Client Settings:**
    *   **TAP Device GUID (NetCfgInstanceID):** This is crucial. It's the unique identifier for your installed TAP adapter instance.
        *   **How to find it:**
            1.  **Device Manager:**
                *   Open Device Manager (type `devmgmt.msc` in Run dialog).
                *   Expand "Network adapters".
                *   Right-click your TAP-Windows adapter (e.g., "TAP-Windows Adapter V9").
                *   Go to the "Details" tab.
                *   From the "Property" dropdown, select "Hardware IDs". One of the listed IDs often contains or is related to the `NetCfgInstanceID`. More reliably, select **"NetCfgInstanceID"** directly from the Property dropdown if available (Windows 10/11). It will look like `{xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx}`.
            2.  **PowerShell (Recommended):**
                *   Open PowerShell.
                *   Run the command: `Get-NetAdapter | Format-List -Property Name,InterfaceDescription,NetCfgInstanceID,ComponentID`
                *   Look for your TAP adapter in the list (e.g., by `InterfaceDescription` like "TAP-Windows Adapter V9").
                *   Copy the `NetCfgInstanceID` value (including the curly braces `{}`).
        *   Enter this exact `NetCfgInstanceID` string into the "TAP Device GUID" field in the application.
    *   **Remote UDP Proxy IP:** The IP address of the server that will receive your encrypted UDP packets.
    *   **Remote UDP Proxy Port:** The port on the remote UDP proxy server.
    *   **Encryption Key (Base64):**
        *   A key will be auto-generated on first run if empty.
        *   You can click "Generate New Key" to create a new one. This key is used for encrypting traffic.
        *   The key is saved in settings for subsequent sessions.
3.  **Local Zero-Rated HTTP Proxy (Optional):**
    *   **Zero-Rated Domain:** The domain your ISP considers zero-rated.
    *   **Proxy Listen Address:** The local address and port for the HTTP proxy to listen on (e.g., `http://0.0.0.0:8080`).

### c. Operation

1.  **Starting the VPN Client:**
    *   After configuring, click "Start VPN Client".
    *   Check the logs for status messages (e.g., "Found TAP device path", "Successfully opened TAP device handle", "TAP media status set to Connected").
    *   If successful, your system's internet traffic should now be routed through the TAP interface (this usually requires manual OS routing configuration - see section 5).
2.  **Stopping the VPN Client:**
    *   Click "Stop VPN Client". Logs should indicate "TAP media status set to Disconnected" and "TAP device closed".
3.  **Starting/Stopping Local HTTP Proxy:** Use the respective buttons.

## 5. System Network Configuration (Important!)

Simply running the VPN client and connecting the TAP adapter is usually not enough. You need to configure your operating system to route traffic through the newly active TAP interface. This typically involves:

1.  **Assigning an IP address to the TAP interface:** After the TAP adapter is "connected" by the VPN client, you (or a script) must assign it an IP address and subnet mask (e.g., `10.8.0.2` with mask `255.255.255.0`). This can be done via:
    *   Network Connections GUI (Control Panel).
    *   `netsh interface ip set address name="Your TAP Adapter Name" static 10.8.0.2 255.255.255.0`
    *   The TAP adapter name can be found in Network Connections or via `Get-NetAdapter` in PowerShell.
2.  **Setting routes:**
    *   You might need to add a route for your remote UDP proxy's IP address to go through your original internet gateway.
    *   You will likely need to change your system's default gateway to be an IP on the TAP interface's subnet (e.g., the IP of the VPN server endpoint, like `10.8.0.1`, if your TAP client is `10.8.0.2`).
    *   These operations are advanced. Tools like `route add` command in Windows or PowerShell's `New-NetRoute` are used.
    *   **Example (Conceptual):** If TAP interface gets IP `10.8.0.2`, and your VPN server is at `10.8.0.1` (via the tunnel), you might change your default route (`0.0.0.0/0`) to use `10.8.0.1` as the gateway, ensuring the physical route to your actual UDP proxy server still uses your physical adapter's gateway.

## 6. Troubleshooting

*   **Check Logs:** The application log panel provides detailed information. Look for errors from SetupAPI, CreateFile, DeviceIoControl.
*   **Run as Administrator:** **Crucial.** Most TAP operations will fail without it.
*   **TAP Driver:** Verify the TAP driver is installed and **enabled** in Device Manager. Try reinstalling it if issues persist.
*   **Correct GUID:** Ensure the `NetCfgInstanceID` in the settings is exact and corresponds to an enabled TAP adapter.
    *   Error: "Failed to find device path for TAP instance GUID" - Check GUID and ensure adapter is enabled.
*   **CreateFile Failures:**
    *   Error: "Access is denied" (Win32 Error 5) - Usually means not running as Administrator.
    *   Error: "The system cannot find the file specified" (Win32 Error 2) - Device path might be incorrect or device disabled.
*   **SetMediaStatus Failures:**
    *   Error: "Failed to set media status" - Could be an issue with the TAP driver or permissions.
*   **Firewall:** Your firewall might block `VpnApp.exe` or traffic on the TAP interface.
*   **Antivirus:** Some antivirus software might interfere with low-level network operations.
*   **IOCTL Compatibility:** If using a very new or very old TAP driver, the IOCTL codes used in `NativeMethods.cs` might not be perfectly compatible.

## 7. Building from Source (For Developers)

1.  Open the `.sln` or `.csproj` file in Visual Studio (e.g., 2022).
2.  Ensure you have the .NET SDK installed (e.g., .NET 6 or .NET 7).
3.  The `TapDevice.cs` and `TapDeviceEnumerator.cs` classes now contain a more complete P/Invoke implementation for Windows SetupAPI (device discovery) and Kernel32 (device I/O, control).
4.  **IOCTL Codes:** The TAP IOCTL codes (e.g., `TAP_IOCTL_SET_MEDIA_STATUS`, `TAP_IOCTL_GET_MAC`) in `NativeMethods.cs` are based on common knowledge, often from older `tap0901` drivers. Modern `tap-windows6` drivers (used by OpenVPN 2.4+) are generally compatible with these specific IOCTLs for basic functions, but advanced configuration might use different mechanisms (like NDIS OIDs, though not implemented here). Test thoroughly with your target TAP driver version.
5.  **P/Invoke Details:** Pay close attention to structure marshalling (e.g., `SP_DEVICE_INTERFACE_DETAIL_DATA_A.cbSize`) and error checking (`Marshal.GetLastWin32Error()`) in `NativeMethods.cs` and `TapDeviceEnumerator.cs` if modifying this low-level code.
6.  Build the project. The executable will be in `bin/Debug/` or `bin/Release/`.
```
