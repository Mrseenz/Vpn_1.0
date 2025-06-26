import socket
import threading
import win32file  # Windows (use `os.open` for Linux)
import win32con
from cryptography.fernet import Fernet

# Zero-rated proxy server (replace with your proxy IP)
PROXY_IP = "your_proxy_server_ip"
PROXY_PORT = 8080

# Encryption
KEY = Fernet.generate_key()
CIPHER = Fernet(KEY)

def encrypt(data):
    return CIPHER.encrypt(data)

def decrypt(data):
    return CIPHER.decrypt(data)

def start_vpn():
    # Open TAP interface (Windows)
    tap = win32file.CreateFile(
        "\\\\.\\Global\\{TAP_GUID}.tap",  # Replace with your TAP GUID
        win32con.GENERIC_READ | win32con.GENERIC_WRITE,
        win32con.FILE_SHARE_READ | win32con.FILE_SHARE_WRITE,
        None,
        win32con.OPEN_EXISTING,
        win32con.FILE_ATTRIBUTE_SYSTEM,
        None
    )

    # UDP socket to proxy
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    sock.bind(("0.0.0.0", 5555))

    def read_tap():
        while True:
            packet = win32file.ReadFile(tap, 4096)[1]
            encrypted = encrypt(packet)
            sock.sendto(encrypted, (PROXY_IP, PROXY_PORT))

    def read_socket():
        while True:
            data, _ = sock.recvfrom(4096)
            decrypted = decrypt(data)
            win32file.WriteFile(tap, decrypted)

    threading.Thread(target=read_tap, daemon=True).start()
    threading.Thread(target=read_socket, daemon=True).start()

if __name__ == "__main__":
    print("VPN Client (Zero-Rated Bypass) Running...")
    start_vpn()
