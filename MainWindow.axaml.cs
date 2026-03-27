using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace NetworkAnalyzer;

public partial class MainWindow : Window
{
    // Хранение загруженных интерфейсов и истории URL
    private NetworkInterface[] _interfaces = Array.Empty<NetworkInterface>();
    private readonly List<string> _urlHistory = new();

    public MainWindow()
    {
        InitializeComponent();
        LoadInterfaces();
    }

    // ======================== Сетевые интерфейсы ========================

    /// <summary>Загрузить список сетевых интерфейсов в ListBox.</summary>
    private void LoadInterfaces()
    {
        _interfaces = NetworkInterface.GetAllNetworkInterfaces();
        InterfacesListBox.Items.Clear();
        foreach (var ni in _interfaces)
            InterfacesListBox.Items.Add($"{ni.Name} ({ni.OperationalStatus})");
    }

    private void RefreshInterfaces_Click(object? sender, RoutedEventArgs e) => LoadInterfaces();

    /// <summary>При выборе интерфейса — показать подробную информацию.</summary>
    private void InterfacesListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        int idx = InterfacesListBox.SelectedIndex;
        if (idx < 0 || idx >= _interfaces.Length) return;

        var ni = _interfaces[idx];
        var sb = new StringBuilder();

        sb.AppendLine($"Имя:             {ni.Name}");
        sb.AppendLine($"Описание:        {ni.Description}");
        sb.AppendLine($"Тип интерфейса:  {ni.NetworkInterfaceType}");
        sb.AppendLine($"Состояние:       {ni.OperationalStatus}");
        sb.AppendLine($"Скорость:        {FormatSpeed(ni.Speed)}");
        sb.AppendLine($"MAC-адрес:       {FormatMac(ni.GetPhysicalAddress())}");

        // IP-адреса и маски подсети
        var ipProps = ni.GetIPProperties();
        foreach (var ua in ipProps.UnicastAddresses)
        {
            sb.AppendLine();
            sb.AppendLine($"  IP-адрес:      {ua.Address}");
            sb.AppendLine($"  Семейство:     {ua.Address.AddressFamily}");
            if (ua.Address.AddressFamily == AddressFamily.InterNetwork)
                sb.AppendLine($"  Маска подсети: {ua.IPv4Mask}");
        }

        // Статистика
        try
        {
            var stats = ni.GetIPStatistics();
            sb.AppendLine();
            sb.AppendLine($"  Получено байт:    {stats.BytesReceived:N0}");
            sb.AppendLine($"  Отправлено байт:  {stats.BytesSent:N0}");
        }
        catch { /* некоторые адаптеры не поддерживают статистику */ }

        InterfaceInfoBox.Text = sb.ToString();
    }

    // ======================== Анализ URL / URI ========================

    /// <summary>Разобрать введённый URL на компоненты.</summary>
    private void ParseUrl_Click(object? sender, RoutedEventArgs e)
    {
        string raw = UrlTextBox?.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(raw))
        {
            UrlResultBox.Text = "Введите URL.";
            return;
        }

        if (!Uri.TryCreate(raw, UriKind.Absolute, out Uri? uri))
        {
            UrlResultBox.Text = "Невозможно разобрать URL. Проверьте формат (например https://example.com).";
            return;
        }

        AddHistory(raw);

        var sb = new StringBuilder();
        sb.AppendLine("═══ Компоненты URL ═══");
        sb.AppendLine($"Схема (протокол): {uri.Scheme}");
        sb.AppendLine($"Хост:             {uri.Host}");
        sb.AppendLine($"Порт:             {uri.Port}");
        sb.AppendLine($"Путь:             {uri.AbsolutePath}");
        sb.AppendLine($"Параметры запроса: {(string.IsNullOrEmpty(uri.Query) ? "(нет)" : uri.Query)}");
        sb.AppendLine($"Фрагмент:         {(string.IsNullOrEmpty(uri.Fragment) ? "(нет)" : uri.Fragment)}");
        sb.AppendLine($"IsLoopback:       {uri.IsLoopback}");
        sb.AppendLine($"IsDefaultPort:    {uri.IsDefaultPort}");
        sb.AppendLine($"IsFile:           {uri.IsFile}");
        sb.AppendLine($"UserInfo:         {(string.IsNullOrEmpty(uri.UserInfo) ? "(нет)" : uri.UserInfo)}");
        sb.AppendLine($"Сегменты:         {string.Join(", ", uri.Segments)}");

        // Определение типа адреса
        sb.AppendLine();
        sb.AppendLine($"Тип адреса:       {DetermineAddressType(uri.Host)}");

        UrlResultBox.Text = sb.ToString();
    }

    /// <summary>Ping до хоста из URL.</summary>
    private async void PingHost_Click(object? sender, RoutedEventArgs e)
    {
        string raw = UrlTextBox?.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(raw)) { UrlResultBox.Text = "Введите URL."; return; }

        string host = raw;
        // Если ввели полный URL — извлечём хост
        if (Uri.TryCreate(raw, UriKind.Absolute, out Uri? uri))
            host = uri.Host;

        AddHistory(raw);
        UrlResultBox.Text = $"Ping {host} ...";

        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(host, 3000);
            var sb = new StringBuilder();
            sb.AppendLine($"═══ Ping: {host} ═══");
            sb.AppendLine($"Статус:      {reply.Status}");
            if (reply.Status == IPStatus.Success)
            {
                sb.AppendLine($"Адрес:       {reply.Address}");
                sb.AppendLine($"Время (мс):  {reply.RoundtripTime}");
                sb.AppendLine($"TTL:         {reply.Options?.Ttl}");
            }
            UrlResultBox.Text = sb.ToString();
        }
        catch (Exception ex)
        {
            UrlResultBox.Text = $"Ошибка ping: {ex.Message}";
        }
    }

    /// <summary>DNS-запрос для хоста из URL.</summary>
    private async void DnsLookup_Click(object? sender, RoutedEventArgs e)
    {
        string raw = UrlTextBox?.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(raw)) { UrlResultBox.Text = "Введите URL."; return; }

        string host = raw;
        if (Uri.TryCreate(raw, UriKind.Absolute, out Uri? uri))
            host = uri.Host;

        AddHistory(raw);
        UrlResultBox.Text = $"DNS-запрос для {host} ...";

        try
        {
            var entry = await Dns.GetHostEntryAsync(host);
            var sb = new StringBuilder();
            sb.AppendLine($"═══ DNS: {host} ═══");
            sb.AppendLine($"HostName: {entry.HostName}");
            sb.AppendLine();
            sb.AppendLine("IP-адреса:");
            foreach (var ip in entry.AddressList)
            {
                string type = DetermineAddressType(ip.ToString());
                sb.AppendLine($"  {ip,-40} [{ip.AddressFamily}]  ({type})");
            }
            if (entry.Aliases.Length > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Aliases:");
                foreach (var alias in entry.Aliases)
                    sb.AppendLine($"  {alias}");
            }
            UrlResultBox.Text = sb.ToString();
        }
        catch (Exception ex)
        {
            UrlResultBox.Text = $"Ошибка DNS: {ex.Message}";
        }
    }

    /// <summary>Двойной клик по истории — подставить URL обратно в поле ввода.</summary>
    private void HistoryListBox_DoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (HistoryListBox.SelectedItem is string s)
            UrlTextBox.Text = s;
    }

    // ======================== Вспомогательные методы ========================

    /// <summary>Определить тип адреса: loopback, локальный, публичный.</summary>
    private static string DetermineAddressType(string hostOrIp)
    {
        if (!IPAddress.TryParse(hostOrIp, out var ip))
        {
            // Может быть доменное имя
            if (hostOrIp.Equals("localhost", StringComparison.OrdinalIgnoreCase))
                return "Loopback (localhost)";
            return "Доменное имя (тип IP определяется после DNS-запроса)";
        }

        if (IPAddress.IsLoopback(ip))
            return "Loopback";

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            byte[] bytes = ip.GetAddressBytes();
            // 10.x.x.x
            if (bytes[0] == 10) return "Локальный (частный, 10.0.0.0/8)";
            // 172.16.0.0 – 172.31.255.255
            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                return "Локальный (частный, 172.16.0.0/12)";
            // 192.168.x.x
            if (bytes[0] == 192 && bytes[1] == 168)
                return "Локальный (частный, 192.168.0.0/16)";
            // 169.254.x.x — link-local
            if (bytes[0] == 169 && bytes[1] == 254)
                return "Link-local (169.254.0.0/16)";
        }

        return "Публичный";
    }

    private void AddHistory(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        // не дублировать последний
        if (_urlHistory.Count > 0 && _urlHistory[^1] == url) return;
        _urlHistory.Add(url);
        HistoryListBox.Items.Add(url);
    }

    private static string FormatMac(PhysicalAddress mac)
    {
        var bytes = mac.GetAddressBytes();
        return bytes.Length == 0
            ? "(нет)"
            : string.Join(":", bytes.Select(b => b.ToString("X2")));
    }

    private static string FormatSpeed(long speedBps)
    {
        if (speedBps <= 0) return "Н/Д";
        if (speedBps >= 1_000_000_000) return $"{speedBps / 1_000_000_000.0:F1} Гбит/с";
        if (speedBps >= 1_000_000) return $"{speedBps / 1_000_000.0:F1} Мбит/с";
        if (speedBps >= 1_000) return $"{speedBps / 1_000.0:F1} Кбит/с";
        return $"{speedBps} бит/с";
    }
}
