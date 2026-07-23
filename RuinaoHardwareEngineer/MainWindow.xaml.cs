using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using RuinaoTesHardware;
using RuinaoTesProtocol.V14;

namespace RuinaoHardwareEngineer;

public partial class MainWindow : Window
{
    private static readonly TesV14ProductInfoLayout EngineerProductInfoLayout =
        TesV14ProductInfoTextCodec.GetLayout(TesV14ProductInfoGrouping.Groups32);
    private readonly object logFileLock = new();
    private readonly string logFilePath = CreateLogFilePath();
    private readonly BackplaneClient client;
    private readonly DispatcherTimer diagnosticRefreshTimer;
    private CancellationTokenSource? diagnosticCancellation;
    private bool isBusy;
    private bool isDiagnosticRunning;
    private bool handshakeSucceeded;

    public ObservableCollection<LogItem> LogItems { get; } = new();
    public ObservableCollection<RawFrameItem> RawFrameItems { get; } = new();
    public ObservableCollection<BatchRegisterItem> BatchRegisterItems { get; } = new();
    public IReadOnlyList<int> ProductInfoGroups { get; } =
        Enumerable.Range(0, 32).ToArray();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        ProductInfoGroupComboBox.SelectedIndex = 0;
        ProductInfoGroupComboBox_SelectionChanged(ProductInfoGroupComboBox, new SelectionChangedEventArgs(
            ComboBox.SelectionChangedEvent,
            Array.Empty<object>(),
            Array.Empty<object>()));

        client = new BackplaneClient(
            new WindowsUsbBackplaneDiscovery(),
            new UsbTestCompatibleBackplaneTransport());
        client.Log += Client_Log;
        client.StateChanged += Client_StateChanged;
        client.RawFrameSent += Client_RawFrameSent;
        client.RawFrameReceived += Client_RawFrameReceived;

        diagnosticRefreshTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Background,
            (_, _) => RefreshDiagnosticSnapshot(), Dispatcher);
        diagnosticRefreshTimer.Start();

        Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;
        UpdateButtons();
        if (!string.IsNullOrEmpty(logFilePath))
        {
            AddLog(new HardwareLogEntry(DateTimeOffset.Now, "LOG", $"本次运行日志：{logFilePath}"));
        }
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await RunUiActionAsync(RefreshDeviceAsync);
    }

    private async void MainWindow_Closed(object? sender, EventArgs e)
    {
        diagnosticCancellation?.Cancel();
        diagnosticRefreshTimer.Stop();
        client.RawFrameSent -= Client_RawFrameSent;
        client.RawFrameReceived -= Client_RawFrameReceived;
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
            handshakeSucceeded = false;
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
            await client.HandshakeAsync(ReadOptions());
            handshakeSucceeded = true;
        });
    }

    private async void DisconnectButton_Click(object sender, RoutedEventArgs e)
    {
        await RunUiActionAsync(() => client.DisconnectAsync());
    }

    private async void ReadProductInfoTextButton_Click(object sender, RoutedEventArgs e)
    {
        await RunUiActionAsync(async () =>
        {
            var groupIndex = ReadProductInfoGroupIndex();
            var result = await client.ReadProductInfoText32Async(groupIndex, ReadOptions());
            ProductInfoTextBox.Text = result.Text;
        });
    }

    private async void WriteProductInfoTextButton_Click(object sender, RoutedEventArgs e)
    {
        await RunUiActionAsync(async () =>
        {
            var expectedText = ProductInfoTextBox.Text;
            var groupIndex = ReadProductInfoGroupIndex();
            var byteCount = TesV14ProductInfoTextCodec.GetUtf8ByteCount(expectedText);
            if (byteCount > EngineerProductInfoLayout.MaximumTextBytes)
            {
                throw new FormatException(
                    $"32组布局每组最多{EngineerProductInfoLayout.MaximumTextBytes}字节，当前为{byteCount}字节。");
            }

            var startAddress = TesV14ProductInfoTextCodec.GetGroupStartAddress(
                TesV14ProductInfoGrouping.Groups32, groupIndex);
            var endAddress = TesV14ProductInfoTextCodec.GetGroupEndAddress(
                TesV14ProductInfoGrouping.Groups32, groupIndex);
            var requiredRegisters = TesV14ProductInfoTextCodec.GetRequiredRegisterCount(expectedText);
            var confirmation = MessageBox.Show(
                $"将覆盖背板字符串组{groupIndex}：\n\n地址：0x{startAddress:X4}～0x{endAddress:X4}\n"
                    + $"UTF-8字节数：{byteCount}\n实际写入寄存器：{requiredRegisters}个（包含0结束符）\n"
                    + "写入请求：一次完成\n写入后：自动回读当前组并比较\n\n是否继续？",
                $"确认写入背板字符串组{groupIndex}",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (confirmation != MessageBoxResult.Yes)
            {
                return;
            }

            var options = ReadOptions();
            await client.WriteProductInfoText32Async(groupIndex, expectedText, options);
            var readBack = await client.ReadProductInfoText32Async(groupIndex, options);
            if (!string.Equals(expectedText, readBack.Text, StringComparison.Ordinal))
            {
                throw new BackplaneConnectionException(
                    $"写入后回读不一致：写入UTF-8 {byteCount}字节，回读{readBack.Utf8ByteCount}字节。");
            }
        });
    }

    private void ProductInfoGroupComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProductInfoGroupRangeText is null || ProductInfoGroupComboBox.SelectedItem is not int groupIndex)
        {
            return;
        }

        var startAddress = TesV14ProductInfoTextCodec.GetGroupStartAddress(
            TesV14ProductInfoGrouping.Groups32, groupIndex);
        var endAddress = TesV14ProductInfoTextCodec.GetGroupEndAddress(
            TesV14ProductInfoGrouping.Groups32, groupIndex);
        ProductInfoGroupRangeText.Text = $"组{groupIndex} · 0x{startAddress:X4}～0x{endAddress:X4} "
            + $"· {EngineerProductInfoLayout.RegistersPerGroup}个寄存器 / {EngineerProductInfoLayout.CapacityBytes}字节";
    }

    private int ReadProductInfoGroupIndex()
    {
        if (ProductInfoGroupComboBox.SelectedItem is int groupIndex)
        {
            return groupIndex;
        }

        throw new InvalidOperationException("请选择0到31之间的字符串组号。");
    }

    private void ProductInfoTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (ProductInfoByteCountText is null)
        {
            return;
        }

        try
        {
            var byteCount = TesV14ProductInfoTextCodec.GetUtf8ByteCount(ProductInfoTextBox.Text);
            ProductInfoByteCountText.Text = $"UTF-8：{byteCount} / {EngineerProductInfoLayout.MaximumTextBytes} 字节";
            ProductInfoByteCountText.Foreground = byteCount <= EngineerProductInfoLayout.MaximumTextBytes
                ? (System.Windows.Media.Brush)FindResource("MutedBrush")
                : System.Windows.Media.Brushes.DarkRed;
        }
        catch (EncoderFallbackException)
        {
            ProductInfoByteCountText.Text = "文本包含无效的Unicode字符";
            ProductInfoByteCountText.Foreground = System.Windows.Media.Brushes.DarkRed;
        }

        UpdateButtons();
    }

    private async void StartHandshakeStabilityButton_Click(object sender, RoutedEventArgs e)
    {
        await RunDiagnosticAsync(async cancellationToken =>
        {
            var requestedCount = ParseIntegerInRange(StabilityCountTextBox.Text, "执行次数", 1, 100000);
            var intervalMilliseconds = ParseIntegerInRange(StabilityIntervalTextBox.Text, "握手间隔", 0, 60000);
            var successfulLatencies = new List<double>(requestedCount);
            var failedCount = 0;
            var completedCount = 0;
            var consecutiveFailures = 0;
            HandshakeStabilityProgressText.Text = $"准备执行 {requestedCount} 次握手…";
            HandshakeStabilityResultText.Text = "统计中…";

            for (var index = 0; index < requestedCount; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var result = await client.HandshakeAsync(ReadOptions(), cancellationToken);
                    successfulLatencies.Add(result.Elapsed.TotalMilliseconds);
                    handshakeSucceeded = true;
                    consecutiveFailures = 0;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    failedCount++;
                    consecutiveFailures++;
                    AddLog(new HardwareLogEntry(
                        DateTimeOffset.Now,
                        "TEST_FAIL",
                        $"稳定性测试第{index + 1}次握手失败：{exception.Message}"));
                }

                completedCount++;
                UpdateHandshakeStatistics(completedCount, requestedCount, failedCount, successfulLatencies);
                if (consecutiveFailures >= 3)
                {
                    AddLog(new HardwareLogEntry(
                        DateTimeOffset.Now,
                        "TEST_STOP",
                        "连续3次握手失败，稳定性测试已提前停止，避免USB异常时持续发送。"));
                    break;
                }

                if (index + 1 < requestedCount && intervalMilliseconds > 0)
                {
                    await Task.Delay(intervalMilliseconds, cancellationToken);
                }
            }

            if (successfulLatencies.Count == 0)
            {
                handshakeSucceeded = false;
            }

            var summary = FormatHandshakeStatistics(completedCount, failedCount, successfulLatencies);
            AddLog(new HardwareLogEntry(DateTimeOffset.Now, "TEST", $"握手稳定性测试完成：{summary}"));
        });
    }

    private void StopDiagnosticButton_Click(object sender, RoutedEventArgs e)
    {
        diagnosticCancellation?.Cancel();
    }

    private async void SequenceCycleButton_Click(object sender, RoutedEventArgs e)
    {
        SequenceCycleResultText.Text = "测试中…";
        await RunUiActionAsync(async () =>
        {
            try
            {
                var expected = new ushort[] { 65534, 65535, 1, 2 };
                var actual = new List<string>(expected.Length);
                client.SetNextSequenceForDiagnostics(expected[0]);
                foreach (var expectedSequence in expected)
                {
                    var result = await client.HandshakeAsync(ReadOptions());
                    if (result.RequestSequence != expectedSequence)
                    {
                        throw new BackplaneConnectionException(
                            $"发送序号循环错误：expected={expectedSequence}, actual={result.RequestSequence}。");
                    }

                    actual.Add($"{result.RequestSequence}(ACK={result.ResponseAckSequence})");
                }

                handshakeSucceeded = true;
                SequenceCycleResultText.Text = $"通过：{string.Join(" → ", actual)}；下一序号={client.NextSequenceForDiagnostics}";
                AddLog(new HardwareLogEntry(DateTimeOffset.Now, "TEST", $"序号循环测试通过：{string.Join(" → ", actual)}。"));
            }
            catch (Exception exception)
            {
                SequenceCycleResultText.Text = $"失败：{exception.Message}";
                throw;
            }
        });
    }

    private async void StartBatchReadButton_Click(object sender, RoutedEventArgs e)
    {
        BatchReadSummaryText.Text = "读取中…";
        await RunUiActionAsync(async () =>
        {
            try
            {
                var startAddress = ParseRegisterAddress(BatchReadStartAddressTextBox.Text);
                var totalCount = ParseIntegerInRange(BatchReadCountTextBox.Text, "总数量", 1, 1024);
                var batchSize = ParseIntegerInRange(BatchReadSizeTextBox.Text, "每批数量", 1, 256);
                if ((long)startAddress + totalCount - 1 > ushort.MaxValue)
                {
                    throw new FormatException("起始地址加总数量超过0xFFFF。");
                }

                BatchRegisterItems.Clear();
                var batchLatencies = new List<double>();
                var options = ReadOptions();
                var rowIndex = 1;
                for (var offset = 0; offset < totalCount; offset += batchSize)
                {
                    var currentCount = Math.Min(batchSize, totalCount - offset);
                    var addresses = Enumerable.Range(startAddress + offset, currentCount)
                        .Select(value => checked((ushort)value))
                        .ToArray();
                    var result = await client.ReadRegistersAsync(
                        TesV14ProtocolConstants.BackplaneAddress,
                        addresses,
                        options);
                    batchLatencies.Add(result.Elapsed.TotalMilliseconds);
                    foreach (var register in result.Registers)
                    {
                        BatchRegisterItems.Add(new BatchRegisterItem(rowIndex++, register));
                    }
                }

                var totalMilliseconds = batchLatencies.Sum();
                BatchReadSummaryText.Text = $"读取成功：0x{startAddress:X4}～0x{startAddress + totalCount - 1:X4} · "
                    + $"{totalCount}个寄存器 · {batchLatencies.Count}批 · 总耗时{totalMilliseconds:F1}ms · "
                    + $"平均每批{batchLatencies.Average():F1}ms";
                AddLog(new HardwareLogEntry(DateTimeOffset.Now, "TEST", BatchReadSummaryText.Text));
            }
            catch (Exception exception)
            {
                BatchReadSummaryText.Text = $"读取失败：{exception.Message}";
                throw;
            }
        });
    }

    private void RawFrameMonitorCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        // 勾选状态在收到下一帧时读取；开关本身不触发任何USB操作。
    }

    private void ClearRawFramesButton_Click(object sender, RoutedEventArgs e)
    {
        RawFrameItems.Clear();
        RawFrameCountText.Text = "0帧";
    }

    private void RefreshDiagnosticsButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshDiagnosticSnapshot();
    }

    private void Client_RawFrameSent(object? sender, UsbWriteCompletedEventArgs entry)
    {
        Dispatcher.BeginInvoke(() => AddRawFrame(RawFrameItem.FromSent(entry)));
    }

    private void Client_RawFrameReceived(object? sender, UsbFrameReceivedEventArgs entry)
    {
        Dispatcher.BeginInvoke(() => AddRawFrame(RawFrameItem.FromReceived(entry)));
    }

    private void AddRawFrame(RawFrameItem item)
    {
        if (RawFrameMonitorCheckBox.IsChecked != true)
        {
            return;
        }

        const int maximumFrames = 500;
        RawFrameItems.Add(item);
        while (RawFrameItems.Count > maximumFrames)
        {
            RawFrameItems.RemoveAt(0);
        }

        RawFrameCountText.Text = $"{RawFrameItems.Count}帧（最多保留{maximumFrames}帧）";
        RawFrameGrid.ScrollIntoView(item);
    }

    private void RefreshDiagnosticSnapshot()
    {
        if (!IsInitialized || UsbDiagnosticText is null || ProtocolDiagnosticText is null)
        {
            return;
        }

        var snapshot = client.GetTransportDiagnosticSnapshot();
        if (snapshot is null)
        {
            UsbDiagnosticText.Text = "当前传输层不提供诊断快照。";
            ProtocolDiagnosticText.Text = $"客户端状态：{client.State}";
            return;
        }

        var device = client.Device;
        UsbDiagnosticText.Text =
            $"设备：{device?.Description ?? "未发现"}\n"
            + $"VID/PID：{TesUsbIdentity.VendorId:X4}:{TesUsbIdentity.ProductId:X4}\n"
            + $"驱动：{device?.DriverStatus ?? "未知"}\n"
            + $"USB句柄：{(snapshot.IsOpen ? "已打开" : "未打开")}\n"
            + $"接收线程：{(snapshot.ReceiveLoopRunning ? "运行中" : "未运行")}\n"
            + $"Bulk OUT：0x{snapshot.BulkOutEndpoint:X2}\n"
            + $"Bulk IN：0x{snapshot.BulkInEndpoint:X2}\n"
            + $"接收字节：{snapshot.ReceivedByteCount}\n"
            + $"接收缓存：{snapshot.BufferedByteCount}字节\n"
            + $"最后发送：{FormatDiagnosticTime(snapshot.LastTransmitTime)}\n"
            + $"最后接收：{FormatDiagnosticTime(snapshot.LastReceiveTime)}";

        ProtocolDiagnosticText.Text =
            $"客户端状态：{client.State}\n"
            + $"协议版本：{ProtocolVersionTextBox.Text.Trim()}\n"
            + $"下一发送序号：{client.NextSequenceForDiagnostics}\n"
            + $"等待ACK序号：{snapshot.PendingSequence?.ToString(CultureInfo.InvariantCulture) ?? "无"}\n"
            + $"TX完整帧：{snapshot.TransmittedFrameCount}\n"
            + $"RX完整帧：{snapshot.ReceivedFrameCount}\n"
            + $"匹配回复：{snapshot.MatchedFrameCount}\n"
            + $"中间ACK：{snapshot.IntermediateAcknowledgementCount}\n"
            + $"迟到/主动帧：{snapshot.UnmatchedFrameCount}\n"
            + $"无效帧：{snapshot.InvalidFrameCount}\n"
            + $"请求超时：{snapshot.ExchangeTimeoutCount}";
    }

    private async Task RunDiagnosticAsync(Func<CancellationToken, Task> action)
    {
        if (isBusy)
        {
            return;
        }

        diagnosticCancellation = new CancellationTokenSource();
        isBusy = true;
        isDiagnosticRunning = true;
        UpdateButtons();
        try
        {
            await action(diagnosticCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            AddLog(new HardwareLogEntry(DateTimeOffset.Now, "TEST_STOP", "诊断测试已由用户停止。"));
            HandshakeStabilityProgressText.Text += " · 已停止";
        }
        catch (Exception exception)
        {
            AddLog(new HardwareLogEntry(DateTimeOffset.Now, "ERROR", exception.Message));
        }
        finally
        {
            diagnosticCancellation.Dispose();
            diagnosticCancellation = null;
            isDiagnosticRunning = false;
            isBusy = false;
            UpdateConnectionStateBadge();
            UpdateButtons();
            RefreshDiagnosticSnapshot();
        }
    }

    private void UpdateHandshakeStatistics(
        int completedCount,
        int requestedCount,
        int failedCount,
        IReadOnlyList<double> successfulLatencies)
    {
        HandshakeStabilityProgressText.Text = $"进度：{completedCount} / {requestedCount}";
        HandshakeStabilityResultText.Text = FormatHandshakeStatistics(completedCount, failedCount, successfulLatencies);
    }

    private static string FormatHandshakeStatistics(
        int completedCount,
        int failedCount,
        IReadOnlyList<double> successfulLatencies)
    {
        var successCount = successfulLatencies.Count;
        var successRate = completedCount == 0 ? 0 : successCount * 100.0 / completedCount;
        if (successCount == 0)
        {
            return $"已执行={completedCount}  成功=0  失败={failedCount}  成功率={successRate:F2}%  延迟=无有效数据";
        }

        var ordered = successfulLatencies.OrderBy(value => value).ToArray();
        var p95Index = Math.Clamp((int)Math.Ceiling(ordered.Length * 0.95) - 1, 0, ordered.Length - 1);
        return $"已执行={completedCount}  成功={successCount}  失败={failedCount}  成功率={successRate:F2}%\n"
            + $"延迟(ms)：min={ordered[0]:F1}  avg={ordered.Average():F1}  P95={ordered[p95Index]:F1}  max={ordered[^1]:F1}";
    }

    private static int ParseIntegerInRange(string text, string fieldName, int minimum, int maximum)
    {
        if (!int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            || value < minimum
            || value > maximum)
        {
            throw new FormatException($"{fieldName}必须在{minimum}到{maximum}之间。");
        }

        return value;
    }

    private static ushort ParseRegisterAddress(string text)
    {
        text = text.Trim();
        var style = NumberStyles.Integer;
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            text = text[2..];
            style = NumberStyles.HexNumber;
        }

        if (!ushort.TryParse(text, style, CultureInfo.InvariantCulture, out var address))
        {
            throw new FormatException("起始地址必须是0x0000到0xFFFF，或对应的十进制数。");
        }

        return address;
    }

    private static string FormatDiagnosticTime(DateTimeOffset? timestamp) =>
        timestamp?.ToLocalTime().ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) ?? "无";

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
        }
        finally
        {
            isBusy = false;
            UpdateConnectionStateBadge();
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
            if (state is BackplaneConnectionState.Disconnected or BackplaneConnectionState.Faulted)
            {
                handshakeSucceeded = false;
                diagnosticCancellation?.Cancel();
            }

            UpdateConnectionStateBadge();
            UpdateButtons();
        });
    }

    private void UpdateConnectionStateBadge()
    {
        StateBadgeText.Text = client.State switch
        {
            BackplaneConnectionState.Disconnected => "未联机",
            BackplaneConnectionState.Connecting => "联机中…",
            BackplaneConnectionState.Handshaking => "握手中…",
            BackplaneConnectionState.Faulted => "联机故障",
            BackplaneConnectionState.Connected when handshakeSucceeded => "仪器已联机",
            BackplaneConnectionState.Connected => "USB已联机 · 待握手",
            _ => client.State.ToString(),
        };
    }

    private void AddLog(HardwareLogEntry entry)
    {
        LogItems.Add(new LogItem(entry));
        PersistLog(entry);
        if (LogItems.Count > 2000)
        {
            LogItems.RemoveAt(0);
        }

        if (LogItems.Count > 0)
        {
            LogList.ScrollIntoView(LogItems[^1]);
        }
    }

    private void PersistLog(HardwareLogEntry entry)
    {
        if (string.IsNullOrEmpty(logFilePath))
        {
            return;
        }

        var line = $"{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{entry.Category}] {entry.Message}";
        if (entry.Bytes is not null)
        {
            line += Environment.NewLine
                + string.Join(' ', entry.Bytes.Select(value => value.ToString("X2", CultureInfo.InvariantCulture)));
        }

        try
        {
            lock (logFileLock)
            {
                File.AppendAllText(logFilePath, line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch
        {
            // 日志落盘失败不能影响硬件操作；界面内存日志仍然保留。
        }
    }

    private static string CreateLogFilePath()
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Ruinao",
                "HardwareEngineer",
                "Logs");
            Directory.CreateDirectory(directory);
            return Path.Combine(directory, $"hardware-engineer-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        }
        catch
        {
            return string.Empty;
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
        var canReadOrWriteProductInfo = !isBusy
            && handshakeSucceeded
            && client.State == BackplaneConnectionState.Connected;
        ReadProductInfoTextButton.IsEnabled = canReadOrWriteProductInfo;
        var productTextFits = false;
        try
        {
            productTextFits = TesV14ProductInfoTextCodec.GetUtf8ByteCount(ProductInfoTextBox.Text)
                <= EngineerProductInfoLayout.MaximumTextBytes;
        }
        catch (EncoderFallbackException)
        {
            // 无效Unicode输入由字节计数提示负责显示，写入按钮保持禁用。
        }
        WriteProductInfoTextButton.IsEnabled = canReadOrWriteProductInfo && productTextFits;
        StartHandshakeStabilityButton.IsEnabled = canReadOrWriteProductInfo;
        SequenceCycleButton.IsEnabled = canReadOrWriteProductInfo;
        StartBatchReadButton.IsEnabled = canReadOrWriteProductInfo;
        StopDiagnosticButton.IsEnabled = isDiagnosticRunning;
    }

    public sealed class BatchRegisterItem
    {
        public int Index { get; }
        public string AddressHex { get; }
        public string ValueHex { get; }
        public uint UnsignedValue { get; }
        public int SignedValue { get; }

        public BatchRegisterItem(int index, TesV14RegisterValue register)
        {
            Index = index;
            AddressHex = $"0x{register.Address:X4}";
            ValueHex = $"0x{register.Value:X8}";
            UnsignedValue = register.Value;
            SignedValue = unchecked((int)register.Value);
        }
    }

    public sealed class RawFrameItem
    {
        public string Time { get; }
        public string Direction { get; }
        public string Command { get; }
        public string Address { get; }
        public ushort SendSequence { get; }
        public ushort AckSequence { get; }
        public string MatchState { get; }
        public string Hex { get; }

        private RawFrameItem(
            DateTimeOffset timestamp,
            string direction,
            byte[] bytes,
            string matchState)
        {
            Time = timestamp.ToLocalTime().ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
            Direction = direction;
            MatchState = matchState;
            Hex = string.Join(' ', bytes.Select(value => value.ToString("X2", CultureInfo.InvariantCulture)));
            if (TesV14ProtocolCodec.TryParseFrame(bytes, out var frame, out _) && frame is not null)
            {
                Command = $"0x{(byte)frame.Command:X2} {frame.Command}";
                Address = $"{frame.SourceAddress:X2}→{frame.DestinationAddress:X2}";
                SendSequence = frame.SendSequence;
                AckSequence = frame.AckSequence;
            }
            else
            {
                Command = "无法解析";
                Address = "—";
            }
        }

        public static RawFrameItem FromSent(UsbWriteCompletedEventArgs entry) =>
            new(entry.Timestamp, "TX", entry.Frame, "已发送");

        public static RawFrameItem FromReceived(UsbFrameReceivedEventArgs entry) =>
            new(
                entry.Timestamp,
                "RX",
                entry.Frame,
                entry.IntermediateAcknowledgement
                    ? "中间ACK"
                    : entry.MatchedRequest ? "已匹配" : "未匹配");
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
