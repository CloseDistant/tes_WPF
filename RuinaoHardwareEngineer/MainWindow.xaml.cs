using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using RuinaoTesHardware;

namespace RuinaoHardwareEngineer;

public partial class MainWindow : Window
{
    private readonly BackplaneClient client;
    private bool isBusy;

    public ObservableCollection<LogItem> LogItems { get; } = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;

        client = new BackplaneClient(
            new WindowsUsbBackplaneDiscovery(),
            new UsbTestCompatibleBackplaneTransport());
        client.Log += Client_Log;
        client.StateChanged += Client_StateChanged;

        Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;
        UpdateButtons();
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await RunUiActionAsync(RefreshDeviceAsync);
    }

    private async void MainWindow_Closed(object? sender, EventArgs e)
    {
        await client.DisposeAsync();
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await RunUiActionAsync(RefreshDeviceAsync);
    }

    private async void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        await RunUiActionAsync(async () =>
        {
            await client.ConnectAsync(ReadOptions());
            await RefreshDeviceAsync();
        });
    }

    private async void HandshakeButton_Click(object sender, RoutedEventArgs e)
    {
        await RunUiActionAsync(async () =>
        {
            // UI只负责发起操作和显示结果。
            // 组帧、USB收发、CRC与ACK校验全部由共享DLL中的BackplaneClient完成，
            // 因此正式WPF主软件也可以调用同一个HandshakeAsync，不需要复制协议代码。
            var result = await client.HandshakeAsync(ReadOptions());
            StateBadgeText.Text = $"握手成功 · {result.Elapsed.TotalMilliseconds:F0} ms";
        });
    }

    private async void DisconnectButton_Click(object sender, RoutedEventArgs e)
    {
        await RunUiActionAsync(() => client.DisconnectAsync());
    }

    private void ClearLogButton_Click(object sender, RoutedEventArgs e)
    {
        LogItems.Clear();
    }

    private async Task RefreshDeviceAsync()
    {
        var device = await client.RefreshDeviceAsync();
        if (device is null)
        {
            DeviceNameText.Text = "未发现背板";
            DriverStatusText.Text = "—";
            DriverStatusText.Foreground = System.Windows.Media.Brushes.DarkRed;
            return;
        }

        DeviceNameText.Text = device.Description;
        HardwareIdText.Text = device.InstanceId;
        DriverStatusText.Text = device.DriverStatus;
        DriverStatusText.Foreground = device.DriverReady
            ? System.Windows.Media.Brushes.SeaGreen
            : System.Windows.Media.Brushes.DarkRed;
    }

    private BackplaneConnectionOptions ReadOptions()
    {
        var versionText = ProtocolVersionTextBox.Text.Trim();
        byte protocolVersion;
        if (versionText.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            if (!byte.TryParse(versionText[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out protocolVersion))
            {
                throw new FormatException("版本字段必须是0x00到0xFF，例如usbtest使用的0x01。");
            }
        }
        else if (!byte.TryParse(versionText, NumberStyles.Integer, CultureInfo.InvariantCulture, out protocolVersion))
        {
            throw new FormatException("版本字段必须是0x00到0xFF，例如usbtest使用的0x01。");
        }

        if (!int.TryParse(TimeoutTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var timeoutMs)
            || timeoutMs is < 100 or > 60000)
        {
            throw new FormatException("超时时间必须在100到60000ms之间。");
        }

        return new BackplaneConnectionOptions(protocolVersion, TimeSpan.FromMilliseconds(timeoutMs));
    }

    private async Task RunUiActionAsync(Func<Task> action)
    {
        if (isBusy)
        {
            return;
        }

        isBusy = true;
        UpdateButtons();
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            AddLog(new HardwareLogEntry(DateTimeOffset.Now, "ERROR", exception.Message));
            StateBadgeText.Text = "操作失败";
        }
        finally
        {
            isBusy = false;
            UpdateButtons();
        }
    }

    private void Client_Log(object? sender, HardwareLogEntry entry)
    {
        Dispatcher.Invoke(() => AddLog(entry));
    }

    private void Client_StateChanged(object? sender, BackplaneConnectionState state)
    {
        Dispatcher.Invoke(() =>
        {
            StateBadgeText.Text = state switch
            {
                BackplaneConnectionState.Disconnected => "未联机",
                BackplaneConnectionState.Connecting => "联机中…",
                BackplaneConnectionState.Connected => "USB已联机",
                BackplaneConnectionState.Handshaking => "握手中…",
                BackplaneConnectionState.Faulted => "联机故障",
                _ => state.ToString(),
            };
            UpdateButtons();
        });
    }

    private void AddLog(HardwareLogEntry entry)
    {
        LogItems.Add(new LogItem(entry));
        if (LogItems.Count > 2000)
        {
            LogItems.RemoveAt(0);
        }

        if (LogItems.Count > 0)
        {
            LogList.ScrollIntoView(LogItems[^1]);
        }
    }

    private void UpdateButtons()
    {
        if (!IsInitialized)
        {
            return;
        }

        RefreshButton.IsEnabled = !isBusy;
        ConnectButton.IsEnabled = !isBusy
            && client.State is BackplaneConnectionState.Disconnected or BackplaneConnectionState.Faulted;
        HandshakeButton.IsEnabled = !isBusy && client.State == BackplaneConnectionState.Connected;
        DisconnectButton.IsEnabled = !isBusy
            && client.State is BackplaneConnectionState.Connected or BackplaneConnectionState.Faulted;
    }

    public sealed class LogItem
    {
        public string Time { get; }
        public string Category { get; }
        public string Message { get; }
        public string Hex { get; }
        public Visibility HexVisibility => string.IsNullOrEmpty(Hex) ? Visibility.Collapsed : Visibility.Visible;

        public LogItem(HardwareLogEntry entry)
        {
            Time = entry.Timestamp.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
            Category = entry.Category;
            Message = entry.Message;
            Hex = entry.Bytes is null
                ? string.Empty
                : string.Join(' ', entry.Bytes.Select(value => value.ToString("X2", CultureInfo.InvariantCulture)));
        }
    }
}
