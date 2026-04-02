using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace pks_kr2
{
    public partial class MainWindow : Window
    {
        private ObservableCollection<string> _urlHistory = new ObservableCollection<string>();
        private ListBox _interfacesListBox;
        private TextBox _urlTextBox;
        private TextBox _resultsTextBox;
        private ListBox _historyListBox;
        
        public MainWindow()
        {
            InitializeComponent();
            
            _interfacesListBox = (ListBox)FindName("InterfacesListBox");
            _urlTextBox = (TextBox)FindName("UrlTextBox");
            _resultsTextBox = (TextBox)FindName("ResultsTextBox");
            _historyListBox = (ListBox)FindName("HistoryListBox");
            
            LoadNetworkInterfaces();
            _historyListBox.ItemsSource = _urlHistory;
        }
        
        public class NetworkInterfaceInfo
        {
            public string DisplayName { get; set; }
            public NetworkInterface NetworkInterface { get; set; }
        }
        
        private void LoadNetworkInterfaces()
        {
            try
            {
                var interfaces = NetworkInterface.GetAllNetworkInterfaces();
                var interfaceList = new List<NetworkInterfaceInfo>();
                
                foreach (var ni in interfaces)
                {
                    interfaceList.Add(new NetworkInterfaceInfo
                    {
                        DisplayName = $"{ni.Name} - {ni.NetworkInterfaceType}",
                        NetworkInterface = ni
                    });
                }
                
                _interfacesListBox.ItemsSource = interfaceList;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки сетевых интерфейсов: {ex.Message}");
            }
        }
        
        private void InterfacesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_interfacesListBox.SelectedItem is NetworkInterfaceInfo selectedInterface)
            {
                DisplayInterfaceInfo(selectedInterface.NetworkInterface);
            }
        }
        
        private void DisplayInterfaceInfo(NetworkInterface networkInterface)
        {
            try
            {
                var ipProperties = networkInterface.GetIPProperties();
                var ipAddresses = new StringBuilder();
                var subnetMasks = new StringBuilder();
                
                foreach (var ip in ipProperties.UnicastAddresses)
                {
                    if (ip.Address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        ipAddresses.AppendLine($"IPv4: {ip.Address}");
                        subnetMasks.AppendLine($"Маска: {ip.IPv4Mask}");
                    }
                    else if (ip.Address.AddressFamily == AddressFamily.InterNetworkV6)
                    {
                        ipAddresses.AppendLine($"IPv6: {ip.Address}");
                    }
                }
                
                var macAddress = networkInterface.GetPhysicalAddress();
                string macString = macAddress != null ? 
                    string.Join(":", macAddress.GetAddressBytes().Select(b => b.ToString("X2"))) : 
                    "Недоступен";
                
                // Находим текстовые поля
                var ipAddressText = (TextBlock)FindName("IpAddressText");
                var subnetMaskText = (TextBlock)FindName("SubnetMaskText");
                var macAddressText = (TextBlock)FindName("MacAddressText");
                var statusText = (TextBlock)FindName("StatusText");
                var speedText = (TextBlock)FindName("SpeedText");
                var interfaceTypeText = (TextBlock)FindName("InterfaceTypeText");
                
                if (ipAddressText != null) ipAddressText.Text = $"IP-адреса:\n{ipAddresses}";
                if (subnetMaskText != null) subnetMaskText.Text = subnetMasks.Length > 0 ? $"Маски подсети:\n{subnetMasks}" : "Маски подсети: Недоступны";
                if (macAddressText != null) macAddressText.Text = $"MAC-адрес: {macString}";
                if (statusText != null) statusText.Text = $"Состояние: {networkInterface.OperationalStatus}";
                if (speedText != null) speedText.Text = $"Скорость: {networkInterface.Speed / 1000000} Мбит/с";
                if (interfaceTypeText != null) interfaceTypeText.Text = $"Тип интерфейса: {networkInterface.NetworkInterfaceType}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка отображения информации об интерфейсе: {ex.Message}");
            }
        }
        
        private void AnalyzeButton_Click(object sender, RoutedEventArgs e)
        {
            string url = _urlTextBox.Text.Trim();
            if (string.IsNullOrEmpty(url))
            {
                MessageBox.Show("Пожалуйста, введите URL для анализа");
                return;
            }
            
            if (!_urlHistory.Contains(url))
            {
                _urlHistory.Add(url);
            }
            
            try
            {
                if (!url.StartsWith("http://") && !url.StartsWith("https://"))
                {
                    url = "http://" + url;
                }
                
                Uri uri = new Uri(url);
                var results = new StringBuilder();
                
                results.AppendLine("=== АНАЛИЗ URL ===");
                results.AppendLine($"Полный URL: {uri.AbsoluteUri}");
                results.AppendLine($"Схема (протокол): {uri.Scheme}");
                results.AppendLine($"Хост: {uri.Host}");
                results.AppendLine($"Порт: {uri.Port}");
                results.AppendLine($"Путь: {uri.AbsolutePath}");
                results.AppendLine($"Параметры запроса: {uri.Query}");
                results.AppendLine($"Фрагмент: {uri.Fragment}");
                results.AppendLine($"\n=== ТИП АДРЕСА ===");
                results.AppendLine(DetermineAddressType(uri.Host));
                
                _resultsTextBox.Text = results.ToString();
            }
            catch (Exception ex)
            {
                _resultsTextBox.Text = $"Ошибка парсинга URL: {ex.Message}";
            }
        }
        
        private async void PingButton_Click(object sender, RoutedEventArgs e)
        {
            string url = _urlTextBox.Text.Trim();
            if (string.IsNullOrEmpty(url))
            {
                MessageBox.Show("Пожалуйста, введите URL или IP адрес для пинга");
                return;
            }
            
            try
            {
                string host = url;
                if (url.Contains("://"))
                {
                    Uri uri = new Uri(url);
                    host = uri.Host;
                }
                
                _resultsTextBox.Text = $"Выполняется пинг {host}...\n";
                
                using (Ping ping = new Ping())
                {
                    var reply = await ping.SendPingAsync(host, 3000);
                    
                    if (reply.Status == IPStatus.Success)
                    {
                        _resultsTextBox.Text += $"✓ Пинг успешен!\nВремя: {reply.RoundtripTime} мс\nTTL: {reply.Options?.Ttl}\nАдрес: {reply.Address}";
                    }
                    else
                    {
                        _resultsTextBox.Text += $"✗ Пинг не удался: {reply.Status}";
                    }
                }
            }
            catch (Exception ex)
            {
                _resultsTextBox.Text = $"Ошибка при пинге: {ex.Message}";
            }
        }
        
        private async void DnsButton_Click(object sender, RoutedEventArgs e)
        {
            string url = _urlTextBox.Text.Trim();
            if (string.IsNullOrEmpty(url))
            {
                MessageBox.Show("Пожалуйста, введите URL или доменное имя");
                return;
            }
            
            try
            {
                string host = url;
                if (url.Contains("://"))
                {
                    Uri uri = new Uri(url);
                    host = uri.Host;
                }
                
                _resultsTextBox.Text = $"Получение DNS информации для {host}...\n";
                
                var addresses = await Dns.GetHostAddressesAsync(host);
                var hostEntry = await Dns.GetHostEntryAsync(host);
                
                var results = new StringBuilder();
                results.AppendLine($"=== DNS ИНФОРМАЦИЯ ===");
                results.AppendLine($"Хост: {host}");
                results.AppendLine($"Каноническое имя: {hostEntry.HostName}");
                results.AppendLine($"\nIP-адреса:");
                
                foreach (var ip in addresses)
                {
                    results.AppendLine($"  {ip} ({(ip.AddressFamily == AddressFamily.InterNetwork ? "IPv4" : "IPv6")})");
                }
                
                results.AppendLine($"\nПсевдонимы:");
                foreach (var alias in hostEntry.Aliases)
                {
                    results.AppendLine($"  {alias}");
                }
                
                _resultsTextBox.Text = results.ToString();
            }
            catch (Exception ex)
            {
                _resultsTextBox.Text = $"Ошибка получения DNS информации: {ex.Message}";
            }
        }
        
        private string DetermineAddressType(string host)
        {
            try
            {
                if (IPAddress.TryParse(host, out IPAddress ipAddress))
                {
                    if (IPAddress.IsLoopback(ipAddress))
                        return "Тип: Loopback (локальный)";
                    
                    if (ipAddress.AddressFamily == AddressFamily.InterNetwork)
                    {
                        byte[] bytes = ipAddress.GetAddressBytes();
                        if (bytes[0] == 10)
                            return "Тип: Локальный (частный) - 10.0.0.0/8";
                        if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                            return "Тип: Локальный (частный) - 172.16.0.0/12";
                        if (bytes[0] == 192 && bytes[1] == 168)
                            return "Тип: Локальный (частный) - 192.168.0.0/16";
                        
                        return "Тип: Публичный адрес";
                    }
                    
                    return "Тип: IPv6 адрес";
                }
                
                if (host.EndsWith(".local") || host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
                    return "Тип: Локальный домен";
                
                return "Тип: Публичный домен";
            }
            catch
            {
                return "Тип: Не удалось определить";
            }
        }
        
        private void ClearHistoryButton_Click(object sender, RoutedEventArgs e)
        {
            _urlHistory.Clear();
            _resultsTextBox.Text = "История очищена.";
        }
        
        private void HistoryListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_historyListBox.SelectedItem is string selectedUrl)
            {
                _urlTextBox.Text = selectedUrl;
                AnalyzeButton_Click(sender, e);
            }
        }
    }
}