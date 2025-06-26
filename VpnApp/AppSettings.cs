using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace VpnApp
{
    public class AppSettings : INotifyPropertyChanged
    {
        private string _tapGuid = "{YOUR-TAP-GUID}";
        private string _proxyIp = "127.0.0.1";
        private string _proxyPortString = "5555"; // Default VPN UDP Port
        private string _encryptionKeyBase64 = "";
        private string _zeroRatedDomain = "free.facebook.com"; // Default HTTP Proxy target
        private string _proxyListenAddress = "http://0.0.0.0:8080"; // Default HTTP Proxy listen

        public string TapGuid { get => _tapGuid; set { if (_tapGuid != value) { _tapGuid = value; OnPropertyChanged(); } } }
        public string ProxyIp { get => _proxyIp; set { if (_proxyIp != value) { _proxyIp = value; OnPropertyChanged(); } } }
        public string ProxyPortString { get => _proxyPortString; set { if (_proxyPortString != value) { _proxyPortString = value; OnPropertyChanged(); } } }
        public string EncryptionKeyBase64 { get => _encryptionKeyBase64; set { if (_encryptionKeyBase64 != value) { _encryptionKeyBase64 = value; OnPropertyChanged(); } } }
        public string ZeroRatedDomain { get => _zeroRatedDomain; set { if (_zeroRatedDomain != value) { _zeroRatedDomain = value; OnPropertyChanged(); } } }
        public string ProxyListenAddress { get => _proxyListenAddress; set { if (_proxyListenAddress != value) { _proxyListenAddress = value; OnPropertyChanged(); } } }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
