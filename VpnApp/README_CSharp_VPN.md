```markdown
# Zero-Rated VPN Tool (C# WPF Version)

## 1. Overview

This application provides a VPN client and an optional local HTTP proxy. The VPN client is designed to capture traffic from a virtual TAP network interface, encrypt it, and send it to a configured UDP proxy server. The local HTTP proxy can forward HTTP traffic to a specified "zero-rated" domain.

**Disclaimer:** This tool is provided for educational purposes to understand VPN, networking, and encryption concepts. Misuse of this tool to bypass ISP terms of service or for any illegal activities is strongly discouraged. Users are responsible for complying with all local laws and ISP policies. The TAP device interaction and network routing require administrator privileges.

## 2. Features

*   VPN client to tunnel traffic via a TAP interface.
*   Traffic encryption between the client and the remote UDP proxy.
*   Configurable remote UDP proxy IP and port.
*   Generates and uses a local encryption key (AES-based).
*   Optional local HTTP proxy to forward requests to a zero-rated domain.
*   GUI for configuration and operation.
*   Saves settings between sessions.
*   Logging of operations.

## 3. Prerequisites

*   **Windows Operating System:** (Tested/developed for Windows).
*   **.NET Desktop Runtime:** Version 6.0 or newer (or as specified by the build). You can download it from the official Microsoft .NET website.
*   **TAP-Windows Adapter:** A virtual TAP network adapter must be installed and enabled. The official OpenVPN package (version 2.4.x or 2.5.x recommended for `tap-windows6` driver) is a common source for this.
    *   Download OpenVPN from [https://openvpn.net/community-downloads/](https://openvpn.net/community-downloads/).
    *   During installation, ensure the "TAP Virtual Ethernet Adapter" component is selected.
*   **Administrator Privileges:** The application must be run as an administrator to control the TAP device and potentially bind the HTTP proxy.

## 4. Setup and Usage

### a. Installation (if distributed as a zip)

1.  Extract the application files (e.g., `VpnApp.exe` and supporting DLLs) to a folder on your computer.
2.  Ensure all prerequisites (see section 3) are installed.

### b. Configuration

1.  **Run `VpnApp.exe` as Administrator.**
2.  **VPN Client Settings:**
    *   **TAP Device GUID:**
        *   You need to find the Component ID (GUID) of your installed TAP-Windows adapter.
        *   Open Device Manager (devmgmt.msc).
        *   Expand "Network adapters".
        *   Right-click your TAP-Windows adapter (e.g., "TAP-Windows Adapter V9").
        *   Go to "Details" tab.
        *   Select "Hardware IDs" or "Compatible IDs" from the Property dropdown. The GUID is part of these strings (e.g., `root\NET\0000` or a long GUID like `{xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx}`). You need to find the specific GUID that the `TapDevice.cs` implementation expects (this part of the C# code is currently a placeholder and needs to be fully implemented to specify how it finds/uses the GUID).
        *   **Note:** The current `TapDevice.cs` is a placeholder. A developer needs to implement the actual TAP interaction logic, which will dictate how this GUID is used or if a different identifier (like interface name or index) is required.
        *   Enter this GUID into the "TAP Device GUID" field.
    *   **Remote UDP Proxy IP:** The IP address of the server that will receive your encrypted UDP packets (this is the other end of your VPN tunnel, not the HTTP proxy).
    *   **Remote UDP Proxy Port:** The port on the remote UDP proxy server.
    *   **Encryption Key (Base64):**
        *   A key will be auto-generated on first run if empty.
        *   You can click "Generate New Key" to create a new one. This key is used to encrypt traffic between this client and the remote UDP proxy. Both ends must use the same key.
        *   The key is saved in settings for subsequent sessions.
3.  **Local Zero-Rated HTTP Proxy (Optional):**
    *   If you want to run the local HTTP proxy (e.g., for testing or if the UDP proxy itself forwards to this):
    *   **Zero-Rated Domain:** The domain your ISP considers zero-rated (e.g., `free.example.com`).
    *   **Proxy Listen Address:** The local address and port for the HTTP proxy to listen on (e.g., `http://0.0.0.0:8080` or `http://localhost:8080`). `0.0.0.0` listens on all interfaces.

### c. Operation

1.  **Starting the VPN Client:**
    *   After configuring, click "Start VPN Client".
    *   Check the logs for status messages.
    *   If successful, your system's internet traffic should now be routed through the TAP interface (this usually requires manual OS routing configuration - see section 5).
2.  **Stopping the VPN Client:**
    *   Click "Stop VPN Client".
3.  **Starting the Local HTTP Proxy:**
    *   Click "Start Local Proxy".
    *   If successful, you can configure applications or your system to use this HTTP proxy (e.g., `127.0.0.1:8080`).
4.  **Stopping the Local HTTP Proxy:**
    *   Click "Stop Local Proxy".

## 5. System Network Configuration (Important!)

Simply running the VPN client is usually not enough. You need to configure your operating system to route traffic through the newly active TAP interface. This typically involves:

1.  **Assigning an IP address to the TAP interface:** This might be done by the `TapDevice.cs` implementation (currently a placeholder) or might need manual configuration in Network Connections.
2.  **Setting routes:**
    *   You might need to add a route for your remote UDP proxy's IP address to go through your original internet gateway.
    *   You might need to change your system's default gateway to be the IP address of the TAP interface (or an IP on the TAP interface's subnet).
    *   These operations are advanced and depend on your network setup. Tools like `route add` command in Windows are used.
    *   **Example (Conceptual):** If TAP interface gets IP `10.0.0.1`, you might set your system's default route to `10.0.0.1`.

## 6. Troubleshooting

*   **Check Logs:** The application log panel provides information about operations and errors.
*   **Run as Administrator:** Ensure the application is always run with administrator privileges.
*   **TAP Driver:** Verify the TAP driver is installed and enabled in Device Manager. Try reinstalling it if issues persist.
*   **Firewall:** Your firewall (Windows Firewall or third-party) might block the application or traffic on the TAP interface. Ensure appropriate rules are in place.
*   **Encryption Key Mismatch:** If connecting to a custom UDP proxy server, ensure the encryption key matches exactly on both client and server.
*   **TAP GUID/Path:** The way the `TapDevice.cs` (once implemented) refers to the TAP adapter is crucial. An incorrect GUID or path will cause failure.

## 7. Building from Source (For Developers)

1.  Open the `.sln` or `.csproj` file in Visual Studio (e.g., 2022).
2.  Ensure you have the .NET SDK installed (e.g., .NET 6 or .NET 7).
3.  Implement the `TapDevice.cs` class with actual Windows TAP driver interaction (P/Invoke calls to `tap-windows6` functions like `TapOpen`, `TapRead`, `TapWrite`, `TapSetIp`, `TapSetStatus`, etc.). This is a complex part.
4.  Build the project.
5.  The executable will be in `bin/Debug/` or `bin/Release/`.
```
