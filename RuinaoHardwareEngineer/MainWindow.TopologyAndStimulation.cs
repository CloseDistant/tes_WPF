using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using RuinaoHardwareEngineer.Features.DeviceTopology;
using RuinaoHardwareEngineer.Features.RawStimulation;
using RuinaoTesHardware;

namespace RuinaoHardwareEngineer;

public partial class MainWindow
{
    private EngineerDeviceTopologyService topologyService = null!;
    private UsbTest4RawStimulationService rawStimulationService = null!;
    private CancellationTokenSource? stimulationConfigurationCancellation;
    private DispatcherTimer? stimulationCountdownTimer;
    private DateTimeOffset stimulationDeadline;
    private byte stimulationCountdownBoardAddress;
    private bool stimulationAutoStopRunning;
    private bool rawConfigurationSent;
    private bool updatingRawWaveformRows;
    private UsbTest4StimulusValueMode stimulationValueMode = UsbTest4StimulusValueMode.DirectDa;

    public ObservableCollection<BoardSlotItem> BoardSlotItems { get; } = new();
    public ObservableCollection<RawWaveformRow> RawWaveformItems { get; } = new();
    public ObservableCollection<BoardAddressOption> OnlineStimulationBoardOptions { get; } = new();
    public IReadOnlyList<BoardAddressOption> BoardAddressOptions { get; } =
        Enumerable.Range(0, 8)
            .Select(index => new BoardAddressOption((byte)index))
            .ToArray();
    public IReadOnlyList<int> StimulationChannels { get; } = Enumerable.Range(1, 8).ToArray();
    public IReadOnlyList<WaveformTypeOption> WaveformTypes { get; } =
    [
        new(1, "1 定值"),
        new(2, "2 正弦"),
        new(3, "3 方波"),
        new(4, "4 三角"),
        new(5, "5 锯齿"),
        new(6, "6 上升"),
        new(7, "7 下降"),
        new(8, "8 梯形"),
        new(9, "9 随机"),
        new(10, "10 电刺激脉冲"),
        new(11, "11 自定义"),
    ];

    private void InitializeTopologyAndStimulation()
    {
        topologyService = new EngineerDeviceTopologyService(client);
        rawStimulationService = new UsbTest4RawStimulationService(client);
        InitializeStimulationImpedance();
        InitializeProductDirectCurrent();
        InitializeProductPulseCurrent();
        ResetTopologyRows();
        BoardAddressComboBox.SelectedIndex = 0;
        StimulationChannelComboBox.SelectedIndex = 0;
        RawWaveformItems.Add(RawWaveformRow.CreateDefault(10));
        RawWaveformItems.Add(RawWaveformRow.CreateDefault(2));
        UpdateRawWaveformIndexes();
        UpdateStimulationValueModeUi();
    }

    private async void ScanTopologyButton_Click(object sender, RoutedEventArgs e)
    {
        await RunUiActionAsync(async () =>
        {
            TopologySummaryText.Text =
                "正在读取背板0x0900槽位位图；只访问位图中已插板的业务板地址…";
            ResetTopologyRows();
            var progress = new Progress<EngineerBoardSlot>(slot =>
            {
                BoardSlotItems[slot.SlotIndex] = new BoardSlotItem(slot);
                TopologySummaryText.Text = $"扫描进度：{slot.SlotIndex + 1}/8";
            });
            var result = await topologyService.ScanAsync(ReadOptions(), progress);
            UpdateImpedanceBoardOptions(result);
            var insertedCount = result.Count(slot => slot.IsInserted);
            var onlineCount = result.Count(slot => slot.IsOnline);
            var stimulationCount = result.Count(slot => slot.BoardKind == EngineerBoardKind.Stimulation);
            TopologySummaryText.Text =
                $"扫描完成：背板报告插板{insertedCount}块，业务板通信正常{onlineCount}块，"
                + $"当前阶段电刺激板{stimulationCount}块，"
                + $"插板但通信异常{result.Count(slot => slot.IsInserted && !slot.IsOnline)}块。";
            AddLog(new HardwareLogEntry(DateTimeOffset.Now, "TOPOLOGY", TopologySummaryText.Text));
        });
    }

    private void ResetTopologyRows()
    {
        BoardSlotItems.Clear();
        for (byte address = 0; address < 8; address++)
        {
            BoardSlotItems.Add(BoardSlotItem.NotScanned(address));
        }
    }

    private void AddWaveformButton_Click(object sender, RoutedEventArgs e)
    {
        if (RawWaveformItems.Count >= UsbTest4RawStimulationLayout.MaximumWaveformCount)
        {
            MessageBox.Show("单通道最多支持30段波形。", "波形数量", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        RawWaveformItems.Add(RawWaveformRow.CreateDefault(2, stimulationValueMode, ReadMaxCurrentMilliampere()));
        UpdateRawWaveformIndexes();
        MarkRawConfigurationDirty();
    }

    private void AddPulseWaveformButton_Click(object sender, RoutedEventArgs e)
    {
        if (RawWaveformItems.Count >= UsbTest4RawStimulationLayout.MaximumWaveformCount)
        {
            MessageBox.Show("单通道最多支持30段波形。", "波形数量", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        RawWaveformItems.Add(RawWaveformRow.CreateDefault(10, stimulationValueMode, ReadMaxCurrentMilliampere()));
        UpdateRawWaveformIndexes();
        MarkRawConfigurationDirty();
    }

    private void RawConfigurationPresetComboBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (!IsInitialized
            || RawConfigurationPresetComboBox.SelectedIndex <= 0
            || RawConfigurationPresetComboBox.SelectedItem is not ComboBoxItem item
            || item.Tag is not string preset)
        {
            return;
        }

        try
        {
            var confirmation = MessageBox.Show(
                "应用快速模板会替换当前波形表格，但不会向硬件下发。是否继续？",
                "应用原始配置模板",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.No);
            if (confirmation != MessageBoxResult.Yes)
            {
                return;
            }

            RawWaveformItems.Clear();
            switch (preset)
            {
                case "UsbTest4Default":
                    RawWaveformItems.Add(RawWaveformRow.CreateDefault(
                        10,
                        stimulationValueMode,
                        ReadMaxCurrentMilliampere()));
                    RawWaveformItems.Add(RawWaveformRow.CreateDefault(
                        2,
                        stimulationValueMode,
                        ReadMaxCurrentMilliampere()));
                    break;
                case "Trapezoid":
                    RawWaveformItems.Add(RawWaveformRow.CreateDefault(
                        8,
                        stimulationValueMode,
                        ReadMaxCurrentMilliampere()));
                    break;
                case "Pulse":
                    RawWaveformItems.Add(RawWaveformRow.CreateDefault(
                        10,
                        stimulationValueMode,
                        ReadMaxCurrentMilliampere()));
                    break;
                case "Clear":
                    break;
                default:
                    throw new InvalidOperationException($"未知快速模板：{preset}。");
            }

            UpdateRawWaveformIndexes();
            MarkRawConfigurationDirty();
        }
        finally
        {
            RawConfigurationPresetComboBox.SelectedIndex = 0;
        }
    }

    private void RemoveWaveformButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = RawWaveformGrid.SelectedItems.Cast<RawWaveformRow>().ToArray();
        foreach (var item in selected)
        {
            RawWaveformItems.Remove(item);
        }

        UpdateRawWaveformIndexes();
        MarkRawConfigurationDirty();
    }

    private void RawWaveformGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e) =>
        MarkRawConfigurationDirty();

    private void RawConfigurationInput_Changed(object sender, EventArgs e) =>
        MarkRawConfigurationDirty();

    private void RawWaveformTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (updatingRawWaveformRows
            || sender is not ComboBox comboBox
            || comboBox.DataContext is not RawWaveformRow row
            || comboBox.SelectedValue is not uint selectedType
            || row.DefaultsForWaveformType == selectedType)
        {
            return;
        }

        try
        {
            updatingRawWaveformRows = true;
            row.ApplyDefault(selectedType, stimulationValueMode, ReadMaxCurrentMilliampere());
            RawWaveformGrid.Items.Refresh();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                exception.Message,
                "波形初始值",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            updatingRawWaveformRows = false;
        }

        MarkRawConfigurationDirty();
    }

    private void ToggleStimulationValueModeButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var maxCurrentMilliampere = ReadMaxCurrentMilliampere();
            var nextMode = stimulationValueMode == UsbTest4StimulusValueMode.DirectDa
                ? UsbTest4StimulusValueMode.Current
                : UsbTest4StimulusValueMode.DirectDa;

            updatingRawWaveformRows = true;
            foreach (var row in RawWaveformItems)
            {
                row.ConvertValueMode(stimulationValueMode, nextMode, maxCurrentMilliampere);
            }

            stimulationValueMode = nextMode;
            RawWaveformGrid.Items.Refresh();
            UpdateStimulationValueModeUi();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                exception.Message,
                "DA/电流模式换算",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            updatingRawWaveformRows = false;
        }

        MarkRawConfigurationDirty();
    }

    private void MaxCurrentMilliampereTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        try
        {
            _ = ReadMaxCurrentMilliampere();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                exception.Message,
                "最大正电流",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            MaxCurrentMilliampereTextBox.Text =
                UsbTest4StimulusValueConverter.DefaultMaxCurrentMilliampere
                    .ToString("0.000", CultureInfo.InvariantCulture);
        }

        MarkRawConfigurationDirty();
    }

    private void UpdateStimulationValueModeUi()
    {
        if (ToggleStimulationValueModeButton is null)
        {
            return;
        }

        var isCurrentMode = stimulationValueMode == UsbTest4StimulusValueMode.Current;
        ToggleStimulationValueModeButton.Content = isCurrentMode ? "切换到DA模式" : "切换到电流模式";
        MaxCurrentMilliampereTextBox.IsEnabled = isCurrentMode;
        AmplitudeColumn.Header = isCurrentMode ? "幅值mA" : "幅值";
        OffsetColumn.Header = isCurrentMode ? "偏置mA" : "偏置";
        LowPositiveColumn.Header = isCurrentMode ? "低位/正相mA" : "低位/正相DA";
        HighNegativeColumn.Header = isCurrentMode ? "高位/负相mA" : "高位/负相DA";
        StimulationValueModeText.Text = isCurrentMode
            ? "当前为电流模式：输入mA，发送前按最大正电流换算为有符号DA。"
            : "当前为DA模式：直接输入原始DA值。";
    }

    private void TestLoadConfirmedCheckBox_Changed(object sender, RoutedEventArgs e) =>
        UpdateButtons();

    private void StimulationChannelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (StimulationEnableMaskTextBox is not null
            && StimulationChannelComboBox.SelectedItem is int channel)
        {
            StimulationEnableMaskTextBox.Text = $"0x{1U << (channel - 1):X2}";
        }

        MarkRawConfigurationDirty();
    }

    private void MarkRawConfigurationDirty()
    {
        rawConfigurationSent = false;
        if (RawStimulationStatusText is not null)
        {
            RawStimulationStatusText.Text = "参数已修改，尚未下发配置。";
            RawStimulationStatusText.Foreground = System.Windows.Media.Brushes.DarkOrange;
        }

        UpdateButtons();
    }

    private async void SendRawConfigurationButton_Click(object sender, RoutedEventArgs e)
    {
        await RunUiActionAsync(async () =>
        {
            var configuration = ReadRawStimulationConfiguration();
            var confirmation = MessageBox.Show(
                $"将向业务板0x{configuration.BoardAddress:X2}、刺激通道{configuration.Channel}下发"
                    + $"{configuration.Waveforms.Count}段usbtest4原始波形。\n\n"
                    + "顺序：逐段波形 → 通道总控制。\n"
                    + "这组原始参数尚未形成产品级安全语义，是否继续？",
                "确认下发原始刺激配置",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (confirmation != MessageBoxResult.Yes)
            {
                return;
            }

            stimulationConfigurationCancellation = new CancellationTokenSource();
            try
            {
                RawStimulationStatusText.Text = "正在逐段下发配置…";
                var result = await rawStimulationService.SendConfigurationAsync(
                    configuration,
                    ReadOptions(),
                    stimulationConfigurationCancellation.Token);
                rawConfigurationSent = true;
                var writes = result.WaveformWrites.Append(result.ControlWrite).ToArray();
                var statusResponseCount = writes.Count(
                    write => write.WriteResponseKind == BackplaneWriteResponseKind.StatusCode);
                RawStimulationStatusText.Text =
                    $"配置回复已匹配：{result.WaveformWrites.Count}段波形 + 1个总控制，"
                    + $"业务板0x{configuration.BoardAddress:X2}，通道{configuration.Channel}；"
                    + (statusResponseCount > 0
                        ? $"{statusResponseCount}帧为临时兼容状态码0（硬件已接受，尚未回读验证）。"
                        : "硬件已返回可解析的正式回复。");
                RawStimulationStatusText.Foreground = System.Windows.Media.Brushes.SeaGreen;
                AddLog(new HardwareLogEntry(DateTimeOffset.Now, "STIM_CONFIG", RawStimulationStatusText.Text));
            }
            finally
            {
                stimulationConfigurationCancellation.Dispose();
                stimulationConfigurationCancellation = null;
            }
        });
    }

    private async void SetAllChannelsHighButton_Click(object sender, RoutedEventArgs e)
    {
        await RunUiActionAsync(async () =>
        {
            var address = ReadSelectedBoardAddress();
            await rawStimulationService.SetAllChannelsHighAsync(address, ReadOptions());
            RawStimulationStatusText.Text = $"业务板0x{address:X2}已返回全通道打开命令回复。";
        });
    }

    private async void StartRawStimulationButton_Click(object sender, RoutedEventArgs e)
    {
        await RunUiActionAsync(async () =>
        {
            if (!rawConfigurationSent)
            {
                throw new InvalidOperationException("当前参数尚未完成下发，禁止开始刺激。");
            }

            if (TestLoadConfirmedCheckBox.IsChecked != true)
            {
                throw new InvalidOperationException("必须先确认当前连接的是测试负载。");
            }

            var address = ReadSelectedBoardAddress();
            var confirmation = MessageBox.Show(
                $"将向业务板0x{address:X2}发送开始刺激命令0x0002。\n"
                    + "请确认输出端连接测试负载，不连接人体。",
                "确认开始真实刺激",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (confirmation != MessageBoxResult.Yes)
            {
                return;
            }

            var totalTimeMs = ParseRawUInt32(TotalTimeMsTextBox.Text, "总运行时间");
            if (totalTimeMs == 0)
            {
                throw new InvalidOperationException("总运行时间为0的硬件语义尚未确认，工程师工具不允许启动无期限刺激。");
            }

            await rawStimulationService.StartAsync(address, ReadOptions());
            StartStimulationCountdown(address, TimeSpan.FromMilliseconds(totalTimeMs));
            RawStimulationStatusText.Text =
                $"业务板0x{address:X2}已返回开始命令回复；软件将在{totalTimeMs}ms后自动发送停止和全通道拉低。"
                + "请结合测量仪器确认真实输出。";
            RawStimulationStatusText.Foreground = System.Windows.Media.Brushes.SeaGreen;
        });
    }

    private async void StartSelectedChannelButton_Click(object sender, RoutedEventArgs e)
    {
        await RunUiActionAsync(async () =>
        {
            if (!rawConfigurationSent)
            {
                throw new InvalidOperationException("当前参数尚未完成下发，禁止开始指定通道刺激。");
            }

            if (TestLoadConfirmedCheckBox.IsChecked != true)
            {
                throw new InvalidOperationException("必须先确认当前连接的是测试负载。");
            }

            var address = ReadSelectedBoardAddress();
            var channel = ReadSelectedStimulationChannel();
            var channelMask = UsbTest4RawStimulationService.GetSingleChannelMask(channel);
            var confirmation = MessageBox.Show(
                $"将向业务板0x{address:X2}发送指定通道开始命令：\n"
                    + $"寄存器0x0002，CH{channel}，写入值0x{channelMask:X8}。\n\n"
                    + "本按钮只发送这一条硬件命令，不自动停止或拉低。\n"
                    + "请确认输出端连接测试负载，不连接人体。",
                "确认开始指定通道",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (confirmation != MessageBoxResult.Yes)
            {
                return;
            }

            await rawStimulationService.StartChannelAsync(
                address,
                channel,
                ReadOptions());
            RawStimulationStatusText.Text =
                $"业务板0x{address:X2}已返回CH{channel}开始命令回复："
                + $"0x0002=0x{channelMask:X8}。未追加其他硬件命令。";
            RawStimulationStatusText.Foreground = System.Windows.Media.Brushes.SeaGreen;
            AddLog(new HardwareLogEntry(
                DateTimeOffset.Now,
                "CHANNEL_START",
                RawStimulationStatusText.Text));
        });
    }

    private async void StopSelectedChannelButton_Click(object sender, RoutedEventArgs e)
    {
        await RunSafetyActionAsync(async () =>
        {
            var address = ReadSelectedBoardAddress();
            var channel = ReadSelectedStimulationChannel();
            var channelMask = UsbTest4RawStimulationService.GetSingleChannelMask(channel);
            await rawStimulationService.StopChannelAsync(
                address,
                channel,
                ReadOptions());
            RawStimulationStatusText.Text =
                $"业务板0x{address:X2}已返回CH{channel}停止命令回复："
                + $"0x0003=0x{channelMask:X8}。未追加全通道拉低或其他硬件命令。";
            RawStimulationStatusText.Foreground = System.Windows.Media.Brushes.SeaGreen;
            AddLog(new HardwareLogEntry(
                DateTimeOffset.Now,
                "CHANNEL_STOP",
                RawStimulationStatusText.Text));
        });
    }

    private async void StopRawStimulationButton_Click(object sender, RoutedEventArgs e)
    {
        stimulationConfigurationCancellation?.Cancel();
        StopStimulationCountdown();
        await RunSafetyActionAsync(async () =>
        {
            var address = ReadSelectedBoardAddress();
            await rawStimulationService.StopAsync(address, ReadOptions());
            RawStimulationStatusText.Text = $"业务板0x{address:X2}已返回停止命令回复。";
            RawStimulationStatusText.Foreground = System.Windows.Media.Brushes.SeaGreen;
        });
    }

    private async void SetAllChannelsLowButton_Click(object sender, RoutedEventArgs e)
    {
        stimulationConfigurationCancellation?.Cancel();
        StopStimulationCountdown();
        await RunSafetyActionAsync(async () =>
        {
            var address = ReadSelectedBoardAddress();
            await rawStimulationService.SetAllChannelsLowAsync(address, ReadOptions());
            RawStimulationStatusText.Text = $"业务板0x{address:X2}已返回全通道拉低命令回复。";
            RawStimulationStatusText.Foreground = System.Windows.Media.Brushes.SeaGreen;
        });
    }

    private async void EmergencyStopAllBoardsButton_Click(object sender, RoutedEventArgs e)
    {
        stimulationConfigurationCancellation?.Cancel();
        StopStimulationCountdown();
        await RunSafetyActionAsync(async () =>
        {
            var onlineAddresses = BoardSlotItems
                .Where(item => item.IsOnline)
                .Select(item => item.AddressValue)
                .Distinct()
                .ToArray();
            if (onlineAddresses.Length == 0)
            {
                onlineAddresses = [ReadSelectedBoardAddress()];
            }

            var failures = new List<string>();
            foreach (var address in onlineAddresses)
            {
                try
                {
                    await rawStimulationService.StopAsync(address, ReadOptions());
                }
                catch (Exception exception)
                {
                    failures.Add($"0x{address:X2}停止失败：{exception.Message}");
                }

                try
                {
                    await rawStimulationService.SetAllChannelsLowAsync(address, ReadOptions());
                }
                catch (Exception exception)
                {
                    failures.Add($"0x{address:X2}拉低失败：{exception.Message}");
                }
            }

            RawStimulationStatusText.Text = failures.Count == 0
                ? $"已向{onlineAddresses.Length}块业务板发送停止和全通道拉低，并收到匹配回复。"
                : $"急停流程已完成，但存在{failures.Count}项失败：{string.Join("；", failures)}";
            RawStimulationStatusText.Foreground = failures.Count == 0
                ? System.Windows.Media.Brushes.SeaGreen
                : System.Windows.Media.Brushes.DarkRed;
            AddLog(new HardwareLogEntry(DateTimeOffset.Now, "EMERGENCY_STOP", RawStimulationStatusText.Text));
        });
    }

    private void StartStimulationCountdown(byte boardAddress, TimeSpan duration)
    {
        StopStimulationCountdown();
        stimulationCountdownBoardAddress = boardAddress;
        stimulationDeadline = DateTimeOffset.UtcNow + duration;
        stimulationCountdownTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(250),
            DispatcherPriority.Normal,
            StimulationCountdownTimer_Tick,
            Dispatcher);
        stimulationCountdownTimer.Start();
    }

    private async void StimulationCountdownTimer_Tick(object? sender, EventArgs e)
    {
        var remaining = stimulationDeadline - DateTimeOffset.UtcNow;
        if (remaining > TimeSpan.Zero)
        {
            RawStimulationStatusText.Text =
                $"刺激计时中：业务板0x{stimulationCountdownBoardAddress:X2}，剩余{remaining.TotalSeconds:F1}s。"
                + "硬件真实输出仍需外部测量确认。";
            return;
        }

        StopStimulationCountdown();
        if (stimulationAutoStopRunning)
        {
            return;
        }

        stimulationAutoStopRunning = true;
        try
        {
            await RunSafetyActionAsync(async () =>
            {
                await rawStimulationService.StopAsync(stimulationCountdownBoardAddress, ReadOptions());
                await rawStimulationService.SetAllChannelsLowAsync(stimulationCountdownBoardAddress, ReadOptions());
                RawStimulationStatusText.Text =
                    $"倒计时结束：业务板0x{stimulationCountdownBoardAddress:X2}已返回停止和全通道拉低回复。";
                RawStimulationStatusText.Foreground = System.Windows.Media.Brushes.SeaGreen;
                AddLog(new HardwareLogEntry(DateTimeOffset.Now, "AUTO_STOP", RawStimulationStatusText.Text));
            });
        }
        finally
        {
            stimulationAutoStopRunning = false;
        }
    }

    private void StopStimulationCountdown()
    {
        if (stimulationCountdownTimer is null)
        {
            return;
        }

        stimulationCountdownTimer.Stop();
        stimulationCountdownTimer.Tick -= StimulationCountdownTimer_Tick;
        stimulationCountdownTimer = null;
    }

    private async Task RunSafetyActionAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            AddLog(new HardwareLogEntry(DateTimeOffset.Now, "SAFETY_ERROR", exception.Message));
            RawStimulationStatusText.Text = $"安全操作失败：{exception.Message}";
            RawStimulationStatusText.Foreground = System.Windows.Media.Brushes.DarkRed;
        }
        finally
        {
            UpdateButtons();
        }
    }

    private UsbTest4RawStimulationConfiguration ReadRawStimulationConfiguration()
    {
        if (RawWaveformItems.Count == 0)
        {
            throw new FormatException("至少需要一段波形。");
        }

        var boardAddress = ReadSelectedBoardAddress();
        var channel = StimulationChannelComboBox.SelectedItem is int selectedChannel
            ? selectedChannel
            : throw new FormatException("请选择刺激通道。");
        var maxCurrentMilliampere = ReadMaxCurrentMilliampere();
        var waveforms = RawWaveformItems
            .Select(item => item.ToModel(stimulationValueMode, maxCurrentMilliampere))
            .ToArray();
        return new UsbTest4RawStimulationConfiguration(
            boardAddress,
            ParseRawUInt32(StimulationEnableMaskTextBox.Text, "通道使能掩码"),
            ParseRawUInt32(StimulationConfigVersionTextBox.Text, "配置版本"),
            channel,
            TriggerEnableCheckBox.IsChecked == true ? 1U : 0U,
            ParseRawUInt32(TriggerSourceTextBox.Text, "触发源掩码"),
            ParseRawUInt32(TotalTimeMsTextBox.Text, "总运行时间"),
            ParseRawUInt32(ChannelFlagsTextBox.Text, "通道标志"),
            waveforms);
    }

    private decimal ReadMaxCurrentMilliampere()
    {
        if (!decimal.TryParse(
                MaxCurrentMilliampereTextBox.Text.Trim(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var value))
        {
            throw new FormatException("最大正电流必须是有效的mA数值。");
        }

        return UsbTest4StimulusValueConverter.ValidateMaxCurrentMilliampere(value);
    }

    private byte ReadSelectedBoardAddress() =>
        BoardAddressComboBox.SelectedItem is BoardAddressOption option
            ? option.Value
            : throw new FormatException("请选择业务板地址。");

    private void UpdateRawWaveformIndexes()
    {
        for (var index = 0; index < RawWaveformItems.Count; index++)
        {
            RawWaveformItems[index].Index = index + 1;
        }

        RawWaveformGrid?.Items.Refresh();
    }

    private static uint ParseRawUInt32(string text, string fieldName)
    {
        text = text.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            if (uint.TryParse(text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hexValue))
            {
                return hexValue;
            }
        }
        else if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var signedValue))
        {
            return unchecked((uint)signedValue);
        }
        else if (uint.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unsignedValue))
        {
            return unsignedValue;
        }

        throw new FormatException($"{fieldName}必须是UInt32、Int32或0x开头的十六进制值。");
    }

    private int ReadSelectedStimulationChannel()
    {
        return StimulationChannelComboBox.SelectedItem is int channel
            ? channel
            : throw new InvalidOperationException("请选择1到8之间的刺激通道。");
    }

    private void UpdateTopologyAndStimulationButtons(bool canUseHardware)
    {
        if (ScanTopologyButton is null)
        {
            return;
        }

        ScanTopologyButton.IsEnabled = canUseHardware;
        SendRawConfigurationButton.IsEnabled = canUseHardware && RawWaveformItems.Count > 0;
        SetAllChannelsHighButton.IsEnabled = canUseHardware;
        StartRawStimulationButton.IsEnabled =
            canUseHardware && rawConfigurationSent && TestLoadConfirmedCheckBox.IsChecked == true;
        StartSelectedChannelButton.IsEnabled =
            canUseHardware && rawConfigurationSent && TestLoadConfirmedCheckBox.IsChecked == true;
        var safetyCommandsAvailable =
            handshakeSucceeded && client.State == BackplaneConnectionState.Connected;
        StopRawStimulationButton.IsEnabled = safetyCommandsAvailable;
        StopSelectedChannelButton.IsEnabled = safetyCommandsAvailable;
        SetAllChannelsLowButton.IsEnabled = safetyCommandsAvailable;
        EmergencyStopAllBoardsButton.IsEnabled = safetyCommandsAvailable;
        UpdateStimulationImpedanceButtons(canUseHardware);
        UpdateProductDirectCurrentButtons(canUseHardware);
    }

    public sealed class BoardSlotItem
    {
        public int SlotIndex { get; }
        public byte AddressValue { get; }
        public string Address { get; }
        public bool IsOnline { get; }
        public string OnlineState { get; }
        public string BoardType { get; }
        public string Identity { get; }
        public string RawRegisters { get; }
        public string Elapsed { get; }
        public string Status { get; }

        public BoardSlotItem(EngineerBoardSlot slot)
        {
            SlotIndex = slot.SlotIndex;
            AddressValue = slot.Address;
            Address = $"0x{slot.Address:X2}";
            IsOnline = slot.IsOnline;
            OnlineState = slot.IsOnline
                ? "在线"
                : slot.IsInserted
                    ? "通信异常"
                    : "空槽位";
            BoardType = slot.BoardKind switch
            {
                EngineerBoardKind.Stimulation => "电刺激板",
                EngineerBoardKind.Eeg => "EEG板",
                _ => slot.IsOnline ? "未知板" : "—",
            };
            Identity = string.IsNullOrWhiteSpace(slot.IdentityText) ? "—" : slot.IdentityText;
            RawRegisters = slot.IdentityRegisters.Count == 0
                ? "—"
                : string.Join(' ', slot.IdentityRegisters.Select(value => $"0x{value:X8}"));
            Elapsed = slot.Elapsed is null ? "—" : $"{slot.Elapsed.Value.TotalMilliseconds:F1}ms";
            Status = slot.StatusMessage;
        }

        public static BoardSlotItem NotScanned(byte address) =>
            new(new EngineerBoardSlot(
                address,
                address,
                false,
                EngineerBoardKind.Unknown,
                string.Empty,
                Array.Empty<uint>(),
                null,
                "尚未扫描"));
    }

    public sealed record BoardAddressOption(byte Value)
    {
        public string Display => $"槽位{Value} / 0x{Value:X2}";
    }

    public sealed record WaveformTypeOption(uint Value, string Display);

    public sealed class RawWaveformRow
    {
        public int Index { get; set; }
        public uint WaveformType { get; set; }
        public uint DefaultsForWaveformType { get; private set; }
        public string DurationUs { get; set; } = "0";
        public string FrequencyHz { get; set; } = "0";
        public string Amplitude { get; set; } = "0";
        public string Offset { get; set; } = "0";
        public string PhaseDegree { get; set; } = "0";
        public string DutyOrder { get; set; } = "0";
        public string LowPositive { get; set; } = "0";
        public string HighNegative { get; set; } = "0";
        public string RisePositive { get; set; } = "0";
        public string HoldInterval { get; set; } = "0";
        public string FallNegative { get; set; } = "0";
        public string CustomPeriod { get; set; } = "0";
        public string SampleCount { get; set; } = "0";
        public string RepeatCount { get; set; } = "1";
        public string Flags { get; set; } = "0";

        public UsbTest4RawWaveform ToModel(
            UsbTest4StimulusValueMode valueMode,
            decimal maxCurrentMilliampere)
        {
            var amplitude = valueMode == UsbTest4StimulusValueMode.Current
                ? UsbTest4StimulusValueConverter.CurrentAmplitudeToRegister(
                    ParseDecimal(Amplitude, $"第{Index}段幅值"),
                    maxCurrentMilliampere)
                : Math.Min(ParseRawUInt32(Amplitude, $"第{Index}段幅值"), (uint)short.MaxValue);
            var offset = valueMode == UsbTest4StimulusValueMode.Current
                ? UsbTest4StimulusValueConverter.CurrentToRegister(
                    ParseDecimal(Offset, $"第{Index}段偏置"),
                    maxCurrentMilliampere)
                : ParseRawUInt32(Offset, $"第{Index}段偏置");

            var lowPositive = ParseRawUInt32(LowPositive, $"第{Index}段低/正值");
            var highNegative = ParseRawUInt32(HighNegative, $"第{Index}段高/负值");
            if (valueMode == UsbTest4StimulusValueMode.Current
                && UsbTest4StimulusValueConverter.UsesAmplitudeLevelCurrent(WaveformType))
            {
                lowPositive = UsbTest4StimulusValueConverter.CurrentToRegister(
                    Math.Abs(ParseDecimal(LowPositive, $"第{Index}段正相值")),
                    maxCurrentMilliampere);
                highNegative = UsbTest4StimulusValueConverter.CurrentToRegister(
                    ParseDecimal(HighNegative, $"第{Index}段负相值"),
                    maxCurrentMilliampere);
            }
            else if (valueMode == UsbTest4StimulusValueMode.Current
                && UsbTest4StimulusValueConverter.UsesSignedLevelCurrent(WaveformType))
            {
                lowPositive = UsbTest4StimulusValueConverter.CurrentToRegister(
                    ParseDecimal(LowPositive, $"第{Index}段低位值"),
                    maxCurrentMilliampere);
                highNegative = UsbTest4StimulusValueConverter.CurrentToRegister(
                    ParseDecimal(HighNegative, $"第{Index}段高位值"),
                    maxCurrentMilliampere);
            }

            return new UsbTest4RawWaveform(
                WaveformType,
                ParseRawUInt32(DurationUs, $"第{Index}段持续时间"),
                ParseRawUInt32(FrequencyHz, $"第{Index}段频率"),
                amplitude,
                offset,
                ParseRawUInt32(PhaseDegree, $"第{Index}段相位"),
                ParseRawUInt32(DutyOrder, $"第{Index}段占空/顺序"),
                lowPositive,
                highNegative,
                ParseRawUInt32(RisePositive, $"第{Index}段升/正时长"),
                ParseRawUInt32(HoldInterval, $"第{Index}段平台/间隔"),
                ParseRawUInt32(FallNegative, $"第{Index}段降/负时长"),
                ParseRawUInt32(CustomPeriod, $"第{Index}段自定义/周期间隔"),
                ParseRawUInt32(SampleCount, $"第{Index}段采样点"),
                ParseRawUInt32(RepeatCount, $"第{Index}段重复次数"),
                ParseRawUInt32(Flags, $"第{Index}段标志"));
        }

        public void ApplyDefault(
            uint waveformType,
            UsbTest4StimulusValueMode valueMode,
            decimal maxCurrentMilliampere)
        {
            var defaults = UsbTest4WaveformDefaults.Create(waveformType);
            WaveformType = defaults.WaveformType;
            DefaultsForWaveformType = defaults.WaveformType;
            DurationUs = FormatUnsigned(defaults.DurationUs);
            FrequencyHz = FormatUnsigned(defaults.FrequencyHz);
            Amplitude = valueMode == UsbTest4StimulusValueMode.Current
                ? FormatCurrent(UsbTest4StimulusValueConverter.RegisterAmplitudeToCurrent(
                    defaults.Amplitude,
                    maxCurrentMilliampere))
                : FormatUnsigned(Math.Min(defaults.Amplitude, (uint)short.MaxValue));
            Offset = FormatRegister(defaults.Offset, valueMode, maxCurrentMilliampere);
            PhaseDegree = FormatUnsigned(defaults.PhaseDegree);
            DutyOrder = FormatUnsigned(defaults.DutyPermilleOrOrder);
            LowPositive = FormatLevel(
                defaults.WaveformType,
                defaults.LowLevelOrPositiveValue,
                valueMode,
                maxCurrentMilliampere);
            HighNegative = FormatLevel(
                defaults.WaveformType,
                defaults.HighLevelOrNegativeValue,
                valueMode,
                maxCurrentMilliampere);
            RisePositive = FormatUnsigned(defaults.RisePermilleOrPositiveDurationUs);
            HoldInterval = FormatUnsigned(defaults.HoldPermilleOrInterphaseIntervalUs);
            FallNegative = FormatUnsigned(defaults.FallPermilleOrNegativeDurationUs);
            CustomPeriod = FormatUnsigned(defaults.CustomIdOrSeedOrPeriodIntervalUs);
            SampleCount = FormatUnsigned(defaults.SampleCount);
            RepeatCount = FormatUnsigned(defaults.RepeatCount);
            Flags = FormatUnsigned(defaults.Flags);
        }

        public void ConvertValueMode(
            UsbTest4StimulusValueMode currentMode,
            UsbTest4StimulusValueMode nextMode,
            decimal maxCurrentMilliampere)
        {
            if (currentMode == nextMode)
            {
                return;
            }

            if (currentMode == UsbTest4StimulusValueMode.DirectDa)
            {
                Amplitude = FormatCurrent(UsbTest4StimulusValueConverter.RegisterAmplitudeToCurrent(
                    ParseRawUInt32(Amplitude, $"第{Index}段幅值"),
                    maxCurrentMilliampere));
                Offset = FormatCurrent(UsbTest4StimulusValueConverter.RegisterToCurrent(
                    ParseRawUInt32(Offset, $"第{Index}段偏置"),
                    maxCurrentMilliampere));
                ConvertLevelRegistersToCurrent(maxCurrentMilliampere);
                return;
            }

            Amplitude = FormatUnsigned(UsbTest4StimulusValueConverter.CurrentAmplitudeToRegister(
                ParseDecimal(Amplitude, $"第{Index}段幅值"),
                maxCurrentMilliampere));
            Offset = FormatSignedRegister(UsbTest4StimulusValueConverter.CurrentToRegister(
                ParseDecimal(Offset, $"第{Index}段偏置"),
                maxCurrentMilliampere));
            ConvertLevelCurrentsToRegisters(maxCurrentMilliampere);
        }

        public static RawWaveformRow CreateDefault(
            uint waveformType,
            UsbTest4StimulusValueMode valueMode = UsbTest4StimulusValueMode.DirectDa,
            decimal maxCurrentMilliampere = UsbTest4StimulusValueConverter.DefaultMaxCurrentMilliampere)
        {
            var row = new RawWaveformRow();
            row.ApplyDefault(waveformType, valueMode, maxCurrentMilliampere);
            return row;
        }

        private void ConvertLevelRegistersToCurrent(decimal maxCurrentMilliampere)
        {
            if (!UsbTest4StimulusValueConverter.UsesSignedLevelCurrent(WaveformType)
                && !UsbTest4StimulusValueConverter.UsesAmplitudeLevelCurrent(WaveformType))
            {
                return;
            }

            var lowCurrent = UsbTest4StimulusValueConverter.RegisterToCurrent(
                ParseRawUInt32(LowPositive, $"第{Index}段低/正值"),
                maxCurrentMilliampere);
            if (UsbTest4StimulusValueConverter.UsesAmplitudeLevelCurrent(WaveformType))
            {
                lowCurrent = Math.Abs(lowCurrent);
            }

            LowPositive = FormatCurrent(lowCurrent);
            HighNegative = FormatCurrent(UsbTest4StimulusValueConverter.RegisterToCurrent(
                ParseRawUInt32(HighNegative, $"第{Index}段高/负值"),
                maxCurrentMilliampere));
        }

        private void ConvertLevelCurrentsToRegisters(decimal maxCurrentMilliampere)
        {
            if (!UsbTest4StimulusValueConverter.UsesSignedLevelCurrent(WaveformType)
                && !UsbTest4StimulusValueConverter.UsesAmplitudeLevelCurrent(WaveformType))
            {
                return;
            }

            var lowCurrent = ParseDecimal(LowPositive, $"第{Index}段低/正值");
            if (UsbTest4StimulusValueConverter.UsesAmplitudeLevelCurrent(WaveformType))
            {
                lowCurrent = Math.Abs(lowCurrent);
            }

            LowPositive = FormatSignedRegister(UsbTest4StimulusValueConverter.CurrentToRegister(
                lowCurrent,
                maxCurrentMilliampere));
            HighNegative = FormatSignedRegister(UsbTest4StimulusValueConverter.CurrentToRegister(
                ParseDecimal(HighNegative, $"第{Index}段高/负值"),
                maxCurrentMilliampere));
        }

        private static string FormatLevel(
            uint waveformType,
            uint rawValue,
            UsbTest4StimulusValueMode valueMode,
            decimal maxCurrentMilliampere)
        {
            if (valueMode == UsbTest4StimulusValueMode.Current
                && (UsbTest4StimulusValueConverter.UsesSignedLevelCurrent(waveformType)
                    || UsbTest4StimulusValueConverter.UsesAmplitudeLevelCurrent(waveformType)))
            {
                var current = UsbTest4StimulusValueConverter.RegisterToCurrent(
                    rawValue,
                    maxCurrentMilliampere);
                if (UsbTest4StimulusValueConverter.UsesAmplitudeLevelCurrent(waveformType))
                {
                    current = Math.Abs(current);
                }

                return FormatCurrent(current);
            }

            return FormatSignedRegister(rawValue);
        }

        private static string FormatRegister(
            uint rawValue,
            UsbTest4StimulusValueMode valueMode,
            decimal maxCurrentMilliampere) =>
            valueMode == UsbTest4StimulusValueMode.Current
                ? FormatCurrent(UsbTest4StimulusValueConverter.RegisterToCurrent(rawValue, maxCurrentMilliampere))
                : FormatSignedRegister(rawValue);

        private static decimal ParseDecimal(string text, string fieldName)
        {
            if (decimal.TryParse(
                    text.Trim(),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var value))
            {
                return value;
            }

            throw new FormatException($"{fieldName}必须是有效的数值。");
        }

        private static string FormatCurrent(decimal value) =>
            Math.Round(value, 3, MidpointRounding.AwayFromZero)
                .ToString("0.###", CultureInfo.InvariantCulture);

        private static string FormatSignedRegister(uint value) =>
            UsbTest4StimulusValueConverter.DecodeSigned(value).ToString(CultureInfo.InvariantCulture);

        private static string FormatUnsigned(uint value) =>
            value.ToString(CultureInfo.InvariantCulture);
    }
}
