using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

// TAP device interaction would require a specific library or P/Invoke calls.
// For example, using a wrapper around OpenVPN's tap-windows6 driver.
// Let's assume a placeholder class 'TapDevice' for now.

public class TapDevice : IDisposable
{
    private string _tapGuid;
    // Placeholder for actual TAP device handle and operations
    private bool _isRunning;
    private Action<string> _logMessageAction;


    public TapDevice(string tapGuid, Action<string> logMessageAction = null)
    {
        _tapGuid = tapGuid;
        _logMessageAction = logMessageAction ?? Console.WriteLine;
        _logMessageAction($"TAP Device: Interface GUID {_tapGuid} (Placeholder - Not functional)");
    }

    public async Task<byte[]> ReadAsync(CancellationToken token)
    {
        // In a real implementation, this would read a packet from the TAP interface.
        // Example: using a FileStream on the TAP device file path obtained from the GUID.
        // For now, simulate some delay and check for cancellation.
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100), token);
        }
        catch (TaskCanceledException)
        {
            _logMessageAction("TAP ReadAsync cancelled during delay.");
            return null;
        }

        if (token.IsCancellationRequested)
        {
            _logMessageAction("TAP ReadAsync cancellation requested.");
            return null;
        }
        _logMessageAction("TAP Read (Placeholder): Generating dummy packet.");
        return Encoding.UTF8.GetBytes($"Placeholder TAP packet data at {DateTime.UtcNow}");
    }

    public async Task WriteAsync(byte[] packet, CancellationToken token)
    {
        // In a real implementation, this would write a packet to the TAP interface.
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(50), token);
        }
        catch (TaskCanceledException)
        {
            _logMessageAction("TAP WriteAsync cancelled during delay.");
            return;
        }

        if (token.IsCancellationRequested)
        {
            _logMessageAction("TAP WriteAsync cancellation requested.");
            return;
        }
        _logMessageAction($"TAP Write (Placeholder): Writing {packet.Length} bytes.");
    }

    public void Start()
    {
        _isRunning = true;
        _logMessageAction("TAP Device Start (Placeholder)");
        // Actual implementation: Open TAP device, set IP, bring interface up.
    }
    public void Stop()
    {
         _isRunning = false;
        _logMessageAction("TAP Device Stop (Placeholder)");
        // Actual implementation: Close TAP device.
    }
    public void Dispose() { Stop(); }
}


public class VpnClientLogic
{
    private UdpClient _udpClient;
    private IPEndPoint _proxyEndPoint;
    private TapDevice _tapDevice;
    private byte[] _encryptionKey;
    private CancellationTokenSource _cancellationTokenSource;

    public event Action<string> LogMessage;

    public VpnClientLogic(string tapGuid, string proxyIp, int proxyPort, byte[] encryptionKey)
    {
        _tapDevice = new TapDevice(tapGuid, (msg) => LogMessage?.Invoke($"[TAP] {msg}"));
        _proxyEndPoint = new IPEndPoint(IPAddress.Parse(proxyIp), proxyPort);
        _encryptionKey = encryptionKey; // This should be securely managed
    }

    public void StartVpn()
    {
        if (_cancellationTokenSource != null && !_cancellationTokenSource.IsCancellationRequested)
        {
            LogMessage?.Invoke("VPN is already running.");
            return;
        }

        _cancellationTokenSource = new CancellationTokenSource();
        // Bind to a specific local port or OS assigned one.
        // Using 0 tells the OS to pick an available ephemeral port.
        _udpClient = new UdpClient(new IPEndPoint(IPAddress.Any, 0));

        _tapDevice.Start();
        LogMessage?.Invoke($"VPN Client Started. UDP listening on {_udpClient.Client.LocalEndPoint}. Proxy target: {_proxyEndPoint}");

        Task.Run(() => ReadFromTapAndSendAsync(_cancellationTokenSource.Token), _cancellationTokenSource.Token);
        Task.Run(() => ReadFromSocketAndWriteAsync(_cancellationTokenSource.Token), _cancellationTokenSource.Token);
    }

    public void StopVpn()
    {
        if (_cancellationTokenSource == null || _cancellationTokenSource.IsCancellationRequested)
        {
            LogMessage?.Invoke("VPN is not running or already stopping.");
            return;
        }

        LogMessage?.Invoke("Stopping VPN Client...");
        _cancellationTokenSource.Cancel();
        _tapDevice.Stop();
        _udpClient?.Close(); // Close will cause ReceiveAsync to throw SocketException if it's active
        _udpClient?.Dispose();
        _udpClient = null;
        LogMessage?.Invoke("VPN Client Stopped.");
    }

    private async Task ReadFromTapAndSendAsync(CancellationToken token)
    {
        LogMessage?.Invoke("TAP listener task started.");
        while (!token.IsCancellationRequested)
        {
            try
            {
                byte[] packet = await _tapDevice.ReadAsync(token);
                if (packet == null || packet.Length == 0)
                {
                    if(token.IsCancellationRequested) break;
                    continue;
                }

                LogMessage?.Invoke($"Read {packet.Length} bytes from TAP.");
                string encryptedDataString = CryptographyHelper.Encrypt(packet, _encryptionKey);
                byte[] dataToSend = Encoding.UTF8.GetBytes(encryptedDataString);

                await _udpClient.SendAsync(dataToSend, dataToSend.Length, _proxyEndPoint);
                LogMessage?.Invoke($"Sent {dataToSend.Length} encrypted bytes to proxy {_proxyEndPoint}.");
            }
            catch (OperationCanceledException)
            {
                LogMessage?.Invoke("TAP reading task cancelled.");
                break;
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke($"Error reading from TAP or sending to proxy: {ex.Message}");
                if (token.IsCancellationRequested) break;
                await Task.Delay(1000, token); // Wait before retrying
            }
        }
        LogMessage?.Invoke("TAP listener task finished.");
    }

    private async Task ReadFromSocketAndWriteAsync(CancellationToken token)
    {
        LogMessage?.Invoke("Socket listener task started.");
        while (!token.IsCancellationRequested)
        {
            try
            {
                if (_udpClient == null || _udpClient.Client == null || !_udpClient.Client.IsBound)
                {
                     LogMessage?.Invoke("UDP client is not available or bound. Exiting socket listener task.");
                     break;
                }

                UdpReceiveResult result = await _udpClient.ReceiveAsync(); // This doesn't accept CancellationToken directly.
                                                                        // Cancellation is handled by closing the socket.
                if (token.IsCancellationRequested) break;

                byte[] receivedData = result.Buffer;
                LogMessage?.Invoke($"Received {receivedData.Length} bytes from proxy {result.RemoteEndPoint}.");

                string encryptedString = Encoding.UTF8.GetString(receivedData);
                byte[] decryptedPacket = CryptographyHelper.Decrypt(encryptedString, _encryptionKey);

                await _tapDevice.WriteAsync(decryptedPacket, token);
                LogMessage?.Invoke($"Wrote {decryptedPacket.Length} decrypted bytes to TAP.");
            }
            catch (SocketException ex) when (_cancellationTokenSource.IsCancellationRequested || _udpClient == null)
            {
                 LogMessage?.Invoke($"Socket receiving cancelled or client closed (SocketErrorCode: {ex.SocketErrorCode}).");
                 break;
            }
            catch (ObjectDisposedException) // Catches if _udpClient is disposed
            {
                LogMessage?.Invoke("Socket receiving cancelled: UdpClient disposed.");
                break;
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke($"Error reading from socket or writing to TAP: {ex.Message}");
                if (token.IsCancellationRequested) break;
                await Task.Delay(1000, token);
            }
        }
        LogMessage?.Invoke("Socket listener task finished.");
    }
}
