using System;
using System.ComponentModel;
using System.Runtime.CompilerServices; // Required for CallerMemberName
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace VpnApp
{
    public partial class MainWindow : Window
    {
        private VpnClientLogic _vpnClient;
        private ZeroRatedProxyLogic _proxyLogic;
        private byte[] _encryptionKey; // Actual key bytes used by logic classes

        public AppSettings Settings { get; set; }

        public MainWindow()
        {
            InitializeComponent();
            ConfigurationService.Initialize(Log); // Pass the Log method to ConfigurationService
            Settings = ConfigurationService.LoadSettings();
            DataContext = Settings; // Bind UI to the AppSettings instance

            // Initial key setup based on loaded settings or generate new if empty
            if (string.IsNullOrWhiteSpace(Settings.EncryptionKeyBase64))
            {
                Log("No encryption key found in settings. Generating a new one.");
                GenerateNewEncryptionKey(false); // Don't log "new key generated" again from here
            }
            else
            {
                UpdateEncryptionKeyBytesFromSettings(true); // Attempt to load key from settings
            }
            Log("Application initialized. Settings loaded.");
        }

        private void UpdateEncryptionKeyBytesFromSettings(bool logSuccess = false)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(Settings.EncryptionKeyBase64))
                {
                    _encryptionKey = Convert.FromBase64String(Settings.EncryptionKeyBase64);
                    if (_encryptionKey.Length != 32)
                    {
                        Log($"Error: Loaded encryption key is invalid (length is {_encryptionKey.Length} bytes, expected 32). Please generate a new key or correct it in settings.");
                        _encryptionKey = null; // Invalidate key
                        Settings.EncryptionKeyBase64 = ""; // Clear invalid key from display/settings
                    }
                    else
                    {
                        if(logSuccess) Log("Encryption key successfully loaded from settings.");
                    }
                }
                else
                {
                    _encryptionKey = null; // No key in settings
                }
            }
            catch (FormatException)
            {
                Log("Error: Invalid Base64 string for encryption key in settings. Please generate a new key or correct it.");
                _encryptionKey = null;
                Settings.EncryptionKeyBase64 = ""; // Clear invalid key
            }
        }

        private void GenerateNewEncryptionKey(bool logAction = true)
        {
            _encryptionKey = CryptographyHelper.GenerateKey();
            Settings.EncryptionKeyBase64 = Convert.ToBase64String(_encryptionKey);
            if (logAction) Log("New encryption key generated and set.");
        }

        private void Log(string message)
        {
            if (Dispatcher.CheckAccess())
            {
                LogListBox.Items.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
                if (LogListBox.Items.Count > 0) LogListBox.ScrollIntoView(LogListBox.Items[LogListBox.Items.Count - 1]);
                StatusTextBlock.Text = message;
            }
            else
            {
                Dispatcher.Invoke(() => Log(message));
            }
        }

        private void GenerateKeyButton_Click(object sender, RoutedEventArgs e)
        {
            GenerateNewEncryptionKey();
        }

        private void StartVpnClientButton_Click(object sender, RoutedEventArgs e)
        {
            UpdateEncryptionKeyBytesFromSettings(); // Ensure _encryptionKey is up-to-date with what's in textbox

            if (_encryptionKey == null || _encryptionKey.Length != 32)
            {
                Log("VPN Client Error: Encryption key is not set or invalid. Please generate or provide a valid 32-byte key (Base64 encoded).");
                return;
            }
            if (string.IsNullOrWhiteSpace(Settings.TapGuid) || Settings.TapGuid == "{YOUR-TAP-GUID}")
            {
                Log("VPN Client Error: TAP GUID is not set in settings.");
                return;
            }
            if (!int.TryParse(Settings.ProxyPortString, out int port) || port <= 0 || port > 65535)
            {
                Log($"VPN Client Error: Invalid Remote UDP Proxy Port '{Settings.ProxyPortString}' in settings.");
                return;
            }
            if (string.IsNullOrWhiteSpace(Settings.ProxyIp))
            {
                Log("VPN Client Error: Remote UDP Proxy IP is not set in settings.");
                return;
            }

            _vpnClient = new VpnClientLogic(Settings.TapGuid, Settings.ProxyIp, port, _encryptionKey);
            _vpnClient.LogMessage += Log;
            try
            {
                _vpnClient.StartVpn();
                Log("VPN Client starting...");
                StartVpnClientButton.IsEnabled = false;
                StopVpnClientButton.IsEnabled = true;
                TapGuidTextBox.IsReadOnly = true;
                ProxyIpTextBox.IsReadOnly = true;
                ProxyPortTextBox.IsReadOnly = true;
                EncryptionKeyTextBox.IsReadOnly = true;
                GenerateKeyButton.IsEnabled = false;
            }
            catch (Exception ex)
            {
                Log($"Failed to start VPN client: {ex.Message}");
                if (_vpnClient != null) _vpnClient.LogMessage -= Log;
                _vpnClient = null;
            }
        }

        private void StopVpnClientButton_Click(object sender, RoutedEventArgs e)
        {
            if (_vpnClient != null)
            {
                _vpnClient.StopVpn();
                _vpnClient.LogMessage -= Log;
                _vpnClient = null;
                Log("VPN Client stopping...");
                StartVpnClientButton.IsEnabled = true;
                StopVpnClientButton.IsEnabled = false;
                TapGuidTextBox.IsReadOnly = false;
                ProxyIpTextBox.IsReadOnly = false;
                ProxyPortTextBox.IsReadOnly = false;
                EncryptionKeyTextBox.IsReadOnly = false;
                GenerateKeyButton.IsEnabled = true;
            }
        }

        private async void StartProxyServerButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(Settings.ZeroRatedDomain))
            {
                Log("Local Proxy Error: Zero-Rated Domain is not set in settings.");
                return;
            }
            if (string.IsNullOrWhiteSpace(Settings.ProxyListenAddress))
            {
                Log("Local Proxy Error: Proxy Listen Address is not set in settings.");
                return;
            }

            _proxyLogic = new ZeroRatedProxyLogic(Settings.ZeroRatedDomain);
            _proxyLogic.LogMessage += Log;
            Log($"Starting local HTTP proxy: Forwarding to '{Settings.ZeroRatedDomain}', Listening on '{Settings.ProxyListenAddress}'");
            try
            {
                await Task.Run(() => _proxyLogic.StartProxyAsync(Settings.ProxyListenAddress));
                // Note: Actual listening confirmation comes from ZeroRatedProxyLogic's log.
                // This just means the task was initiated.
                Log("Local HTTP proxy server process initiated.");
                StartProxyServerButton.IsEnabled = false;
                StopProxyServerButton.IsEnabled = true;
                ZeroRatedDomainTextBox.IsReadOnly = true;
                ProxyListenAddressTextBox.IsReadOnly = true;
            }
            catch (Exception ex)
            {
                Log($"Failed to start local proxy server: {ex.Message}");
                if (_proxyLogic != null) _proxyLogic.LogMessage -= Log;
                _proxyLogic = null;
                StartProxyServerButton.IsEnabled = true;
                StopProxyServerButton.IsEnabled = false;
            }
        }

        private void StopProxyServerButton_Click(object sender, RoutedEventArgs e)
        {
            if (_proxyLogic != null)
            {
                _proxyLogic.StopProxy();
                _proxyLogic.LogMessage -= Log;
                _proxyLogic = null;
                Log("Local HTTP proxy server stopping...");
                StartProxyServerButton.IsEnabled = true;
                StopProxyServerButton.IsEnabled = false;
                ZeroRatedDomainTextBox.IsReadOnly = false;
                ProxyListenAddressTextBox.IsReadOnly = false;
            }
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            Log("Application closing. Stopping services and saving settings...");
            _vpnClient?.StopVpn();
            _proxyLogic?.StopProxy();
            ConfigurationService.SaveSettings(Settings);
            // Log("Settings saved.") is called by ConfigurationService now.
            base.OnClosing(e);
        }
    }
}
