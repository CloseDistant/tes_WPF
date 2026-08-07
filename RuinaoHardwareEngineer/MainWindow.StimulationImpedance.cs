using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using RuinaoHardwareEngineer.Features.DeviceTopology;
using RuinaoHardwareEngineer.Features.StimulationImpedance;
using RuinaoTesHardware;

namespace RuinaoHardwareEngineer;

public partial class MainWindow
{
    private static readonly TimeSpan StimulationImpedanceRefreshInterval =
        TimeSpan.FromSeconds(2);

    private EngineerStimulationImpedanceService stimulationImpedanceService = null!;
    private DispatcherTimer stimulationImpedanceTimer = null!;
    private CancellationTokenSource? stimulationImpedanceCancellation;
    private bool stimulationImpedanceReadRunning;

    public ObservableCollection<StimulationImpedanceChannelItem> StimulationImpedanceItems { get; } = new();

    private void InitializeStimulationImpedance()
    {
        stimulationImpedanceService = new EngineerStimulationImpedanceService(client);
        // 只创建定时器，不在窗口启动时自动运行。
        // 必须先扫描到在线业务板，再由用户点击“每2秒自动读取”显式启动。
        stimulationImpedanceTimer = new DispatcherTimer(
            DispatcherPriority.Background,
            Dispatcher)
        {
            Interval = StimulationImpedanceRefreshInterval,
        };
        stimulationImpedanceTimer.Tick += StimulationImpedanceTimer_Tick;
        ResetStimulationImpedanceRows();
    }

    private void UpdateImpedanceBoardOptions(IReadOnlyList<EngineerBoardSlot> slots)
    {
        StopStimulationImpedanceMonitoring("拓扑已更新，自动读取已停止。");
        var previousImpedanceAddress =
            (ImpedanceBoardComboBox.SelectedItem as BoardAddressOption)?.Value;
        var previousTdcsAddress =
            (ProductTdcsBoardAddressComboBox.SelectedItem as BoardAddressOption)?.Value;
        var previousMtpcsAddress =
            (ProductMtpcsBoardAddressComboBox.SelectedItem as BoardAddressOption)?.Value;
        var previousTpcsAddress =
            (ProductTpcsBoardAddressComboBox.SelectedItem as BoardAddressOption)?.Value;
        OnlineStimulationBoardOptions.Clear();

        // 当前阶段接入的业务板均为电刺激板；各产品功能共用同一份在线槽位快照。
        foreach (var slot in slots.Where(slot => slot.IsOnline))
        {
            OnlineStimulationBoardOptions.Add(new BoardAddressOption(slot.Address));
        }

        ImpedanceBoardComboBox.SelectedItem = FindOnlineBoard(previousImpedanceAddress);
        ProductTdcsBoardAddressComboBox.SelectedItem = FindOnlineBoard(previousTdcsAddress);
        ProductMtpcsBoardAddressComboBox.SelectedItem = FindOnlineBoard(previousMtpcsAddress);
        ProductTpcsBoardAddressComboBox.SelectedItem = FindOnlineBoard(previousTpcsAddress);
        InvalidateProductDirectCurrentConfiguration(
            "拓扑已更新，原产品tDCS配置状态已失效，请重新生成并下发。");
        InvalidateProductMtpcsConfiguration(
            "拓扑已更新，原产品M-tPCS配置状态已失效，请重新生成并下发。");
        ImpedanceStatusText.Text = OnlineStimulationBoardOptions.Count == 0
            ? "拓扑中没有在线业务板，请先确认硬件连接并重新扫描。"
            : $"已关联{OnlineStimulationBoardOptions.Count}个在线电刺激业务板槽位；请选择后读取CH1～CH8。";
        ResetStimulationImpedanceRows();
        UpdateButtons();
    }

    private async void ReadStimulationImpedanceButton_Click(object sender, RoutedEventArgs e)
    {
        await RunUiActionAsync(() => ReadSelectedStimulationImpedanceAsync());
    }

    private async void StartStimulationImpedanceMonitoringButton_Click(object sender, RoutedEventArgs e)
    {
        if (stimulationImpedanceTimer.IsEnabled)
        {
            return;
        }

        stimulationImpedanceCancellation?.Dispose();
        var monitoringCancellation = new CancellationTokenSource();
        stimulationImpedanceCancellation = monitoringCancellation;
        var firstReadSucceeded = false;
        await RunUiActionAsync(async () =>
        {
            await ReadSelectedStimulationImpedanceAsync(monitoringCancellation.Token);
            firstReadSucceeded = true;
        });
        if (!firstReadSucceeded
            || monitoringCancellation.IsCancellationRequested
            || ImpedanceBoardComboBox.SelectedItem is not BoardAddressOption)
        {
            StopStimulationImpedanceMonitoring(
                firstReadSucceeded ? "自动读取未启动。" : "首次读取失败，未启动自动读取。");
            return;
        }

        stimulationImpedanceTimer.Start();
        ImpedanceStatusText.Text += "；已开启每2秒自动读取。";
        UpdateButtons();
    }

    private void StopStimulationImpedanceMonitoringButton_Click(object sender, RoutedEventArgs e)
    {
        StopStimulationImpedanceMonitoring("已停止软件自动读取，未向硬件发送停止命令。");
        UpdateButtons();
    }

    private void ImpedanceBoardComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsInitialized || ImpedanceStatusText is null)
        {
            return;
        }

        StopStimulationImpedanceMonitoring("业务板槽位已切换，自动读取已停止。");
        ResetStimulationImpedanceRows();
        if (ImpedanceBoardComboBox.SelectedItem is BoardAddressOption option)
        {
            ImpedanceStatusText.Text =
                $"已选择槽位{option.Value} / 业务板0x{option.Value:X2}，等待读取CH1～CH8。";
        }

        UpdateButtons();
    }

    private async void StimulationImpedanceTimer_Tick(object? sender, EventArgs e)
    {
        if (isBusy || stimulationImpedanceReadRunning)
        {
            return;
        }

        var cancellationToken = stimulationImpedanceCancellation?.Token ?? CancellationToken.None;
        await RunUiActionAsync(() => ReadSelectedStimulationImpedanceAsync(cancellationToken));
    }

    private async Task ReadSelectedStimulationImpedanceAsync(
        CancellationToken cancellationToken = default)
    {
        if (ImpedanceBoardComboBox.SelectedItem is not BoardAddressOption board)
        {
            throw new InvalidOperationException("请先扫描拓扑并选择在线电刺激业务板槽位。");
        }

        stimulationImpedanceReadRunning = true;
        try
        {
            var snapshot = await stimulationImpedanceService.ReadAsync(
                board.Value,
                ReadOptions(),
                cancellationToken);
            for (var index = 0; index < snapshot.Channels.Count; index++)
            {
                StimulationImpedanceItems[index] =
                    new StimulationImpedanceChannelItem(snapshot.Channels[index], snapshot.ReadTime);
            }

            ImpedanceStatusText.Text =
                $"读取成功：槽位{board.Value} / 业务板0x{board.Value:X2}，"
                + $"CH1～CH8共{snapshot.Channels.Count}个原始值，"
                + $"耗时{snapshot.Elapsed.TotalMilliseconds:F1}ms，seq={snapshot.RequestSequence}。";
            AddLog(new HardwareLogEntry(
                DateTimeOffset.Now,
                "IMPEDANCE",
                ImpedanceStatusText.Text));
        }
        finally
        {
            stimulationImpedanceReadRunning = false;
        }
    }

    private void StopStimulationImpedanceMonitoring(string? status)
    {
        stimulationImpedanceTimer?.Stop();
        stimulationImpedanceCancellation?.Cancel();
        stimulationImpedanceCancellation?.Dispose();
        stimulationImpedanceCancellation = null;
        if (!string.IsNullOrWhiteSpace(status) && ImpedanceStatusText is not null)
        {
            ImpedanceStatusText.Text = status;
        }
    }

    private void ResetStimulationImpedanceRows()
    {
        StimulationImpedanceItems.Clear();
        for (var channel = 1; channel <= 8; channel++)
        {
            StimulationImpedanceItems.Add(StimulationImpedanceChannelItem.Empty(channel));
        }
    }

    private void UpdateStimulationImpedanceButtons(bool canUseHardware)
    {
        if (ReadStimulationImpedanceButton is null)
        {
            return;
        }

        var hasBoard = ImpedanceBoardComboBox.SelectedItem is BoardAddressOption;
        ReadStimulationImpedanceButton.IsEnabled =
            canUseHardware && hasBoard && !stimulationImpedanceReadRunning;
        StartStimulationImpedanceMonitoringButton.IsEnabled =
            canUseHardware
            && hasBoard
            && !stimulationImpedanceReadRunning
            && !stimulationImpedanceTimer.IsEnabled;
        StopStimulationImpedanceMonitoringButton.IsEnabled = stimulationImpedanceTimer.IsEnabled;
    }

    private BoardAddressOption? FindOnlineBoard(byte? previousAddress)
    {
        var previous = previousAddress.HasValue
            ? OnlineStimulationBoardOptions.FirstOrDefault(
                option => option.Value == previousAddress.Value)
            : null;
        return previous ?? OnlineStimulationBoardOptions.FirstOrDefault();
    }

    public sealed class StimulationImpedanceChannelItem
    {
        public int Channel { get; }
        public string ChannelDisplay => $"CH{Channel}";
        public string RegisterAddress { get; }
        public string RawHex { get; }
        public string RawUnsigned { get; }
        public string ImpedanceOhms { get; }
        public string UpdateTime { get; }

        private StimulationImpedanceChannelItem(
            int channel,
            string registerAddress,
            string rawHex,
            string rawUnsigned,
            string impedanceOhms,
            string updateTime)
        {
            Channel = channel;
            RegisterAddress = registerAddress;
            RawHex = rawHex;
            RawUnsigned = rawUnsigned;
            ImpedanceOhms = impedanceOhms;
            UpdateTime = updateTime;
        }

        public StimulationImpedanceChannelItem(
            EngineerStimulationImpedanceChannel channel,
            DateTimeOffset readTime)
            : this(
                channel.Channel,
                $"0x{channel.RegisterAddress:X4}",
                $"0x{channel.RawValue:X8}",
                channel.RawValue.ToString(CultureInfo.InvariantCulture),
                channel.ImpedanceOhms.ToString("0.00", CultureInfo.InvariantCulture),
                readTime.ToLocalTime().ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture))
        {
        }

        public static StimulationImpedanceChannelItem Empty(int channel) =>
            new(channel, $"0x{0x1000 + channel:X4}", "—", "—", "—", "—");
    }
}
