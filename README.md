# Zero-Rated VPN Tunnel

## Description

This project implements a basic VPN client and proxy server designed to bypass internet charges by routing traffic through a zero-rated domain. The VPN client captures network traffic from a TAP interface, encrypts it, and sends it to a proxy server. The proxy server then forwards this traffic to a pre-configured zero-rated domain, effectively masking the origin of the traffic and potentially allowing free internet access if the zero-rated domain is whitelisted by the ISP.

## Features

-   **TAP Interface Integration:** Captures outgoing packets and injects incoming packets using a virtual TAP network interface.
-   **Traffic Encryption:** Encrypts traffic between the client and proxy using the Fernet symmetric encryption library.
-   **UDP Tunneling:** Transports encrypted IP packets over UDP between the client and the proxy.
-   **HTTP Proxying:** The proxy server forwards HTTP requests to a specified zero-rated domain.

## Requirements

-   Python 3
-   `cryptography` library (`pip install cryptography`)
-   `requests` library (`pip install requests`)
-   `pywin32` library (for Windows TAP interface control: `pip install pywin32`)
-   A configured TAP interface.

## Setup and Usage

### 1. Install Dependencies

```bash
pip install cryptography requests pywin32
```

### 2. Configure TAP Interface

You need to have a TAP (Test Access Point) virtual network interface installed and configured on the machine running the VPN client. The exact steps for this vary by operating system.

-   **Windows:** You can use OpenVPN's TAP driver. After installation, you'll need to find the GUID of your TAP adapter.
-   **Linux:** You can use `tunctl` or similar tools to create a `tap` interface. (Note: The current `vpn_client_zero_rated.py` is written for Windows using `win32file`. Modifications would be needed for Linux.)

### 3. Configure the Scripts

**`vpn_client_zero_rated.py`:**

-   Replace `"your_proxy_server_ip"` with the actual IP address of the machine where `zero_rated_proxy.py` will run.
    ```python
    PROXY_IP = "your_proxy_server_ip"
    ```
-   Replace `"\\\\.\\Global\\{TAP_GUID}.tap"` with the correct path to your TAP interface device. For Windows, this involves finding the correct GUID for your TAP adapter.
    ```python
    tap = win32file.CreateFile(
        "\\\\.\\Global\\{YOUR_TAP_GUID}.tap", # Replace {YOUR_TAP_GUID}
        # ...
    )
    ```
    (For Linux, this section would need to be rewritten to use `os.open` with the appropriate TAP device path, e.g., `/dev/net/tun`).

**`zero_rated_proxy.py`:**

-   Replace `"free.facebook.com"` (or the current placeholder) with the zero-rated domain you intend to use. This domain must be accessible without data charges from your ISP.
    ```python
    ZERO_RATED_DOMAIN = "your_zero_rated_domain.com"
    ```

### 4. Run the Proxy Server

On the server machine (or the machine acting as the proxy):

```bash
python zero_rated_proxy.py
```
This will start the HTTP proxy server, typically on port 8080.

### 5. Run the VPN Client

On the client machine (the one whose traffic you want to tunnel):

```bash
python vpn_client_zero_rated.py
```
This will start the VPN client, capture traffic from the configured TAP interface, encrypt it, and send it to the proxy.

### 6. Configure System to Use TAP Interface

After the client is running, you'll need to configure your operating system's routing table to send traffic through the TAP interface. For example, you might set the TAP interface as your default gateway or add specific routes.

## Disclaimer

**Ethical and Legal Considerations:**

-   Using this tool to bypass ISP charges may violate your Internet Service Provider's Terms of Service.
-   Accessing services through such methods might have legal implications depending on your jurisdiction and the specific services accessed.
-   This project is provided for educational purposes to understand networking and encryption concepts. The creators are not responsible for any misuse or any legal or ethical consequences that may arise from its use.
-   Always ensure you are compliant with local laws and your ISP's policies.
