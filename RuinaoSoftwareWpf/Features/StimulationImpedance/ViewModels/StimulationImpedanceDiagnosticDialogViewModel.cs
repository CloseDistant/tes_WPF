using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace RuinaoSoftwareWpf;

public sealed class StimulationImpedanceDiagnosticChannelViewModel
{
    public StimulationImpedanceDiagnosticChannelViewModel(
        StimulationImpedanceChannelSnapshot snapshot)
    {
        Snapshot = snapshot;
        ChannelText = $"CH {snapshot.LogicalChannelNumber}";
        Status = StimulationImpedancePresentation.GetStatus(snapshot.ImpedanceOhms);
        StatusBrush = StimulationImpedancePresentation.GetImpedanceBrush(Status);
        StatusText = Status switch
        {
            StimulationImpedanceStatus.Normal => "阻抗正常",
            StimulationImpedanceStatus.Warning => "阻抗偏高",
            StimulationImpedanceStatus.Critical => "阻抗过高",
            _ => "阻抗不可用",
        };
        ImpedanceText = FormatImpedance(snapshot.ImpedanceOhms);
        BoardText = snapshot.BoardSlotIndex.HasValue && snapshot.BoardAddress.HasValue
            ? $"槽位{snapshot.BoardSlotIndex.Value} / 0x{snapshot.BoardAddress.Value:X2}"
            : "—";
        PhysicalChannelText = snapshot.PhysicalChannelNumber.HasValue
            ? $"CH{snapshot.PhysicalChannelNumber.Value}"
            : "—";
        LastReadText = snapshot.LastSuccessfulReadAt?.ToLocalTime().ToString("HH:mm:ss") ?? "—";
        RegisterText = snapshot.RegisterAddress.HasValue
            ? $"0x{snapshot.RegisterAddress.Value:X4}"
            : "—";
        RawHexText = snapshot.RawValue.HasValue
            ? $"0x{snapshot.RawValue.Value:X8}"
            : "—";
        RawDecimalText = snapshot.RawValue?.ToString(CultureInfo.InvariantCulture) ?? "—";
        ConversionText = snapshot.RawValue switch
        {
            0 => "原始值 0 → 不可用",
            { } raw => $"{raw} ÷ 100 = {snapshot.ImpedanceOhms:0.##} Ω",
            _ => "—",
        };
    }

    public StimulationImpedanceChannelSnapshot Snapshot { get; }
    public int LogicalChannelNumber => Snapshot.LogicalChannelNumber;
    public string ChannelText { get; }
    public StimulationImpedanceStatus Status { get; }
    public Brush StatusBrush { get; }
    public string StatusText { get; }
    public string ImpedanceText { get; }
    public string BoardText { get; }
    public string PhysicalChannelText { get; }
    public string LastReadText { get; }
    public string RegisterText { get; }
    public string RawHexText { get; }
    public string RawDecimalText { get; }
    public string ConversionText { get; }

    private static string FormatImpedance(decimal? impedanceOhms)
    {
        if (!impedanceOhms.HasValue)
        {
            return "—";
        }

        return impedanceOhms.Value >= 1_000m
            ? $"{impedanceOhms.Value / 1_000m:0.00} kΩ"
            : $"{impedanceOhms.Value:0.##} Ω";
    }
}

public sealed class StimulationImpedanceDiagnosticDialogViewModel : ObservableObject
{
    private readonly IHardwareService hardwareService;
    private readonly ILoggingService logger;
    private readonly AsyncRelayCommand refreshCommand;
    private StimulationImpedanceDiagnosticChannelViewModel? selectedChannel;
    private string summaryText = "尚未取得阻抗数据。";
    private string refreshFeedbackText = string.Empty;
    private string errorText = string.Empty;

    public StimulationImpedanceDiagnosticDialogViewModel(
        IHardwareService hardwareService,
        ILoggingService logger)
    {
        this.hardwareService = hardwareService;
        this.logger = logger;
        refreshCommand = new AsyncRelayCommand(
            RefreshAsync,
            () => hardwareService.IsConnected,
            HandleRefreshError);
        RefreshCommand = refreshCommand;
        SelectChannelCommand = new RelayCommand(parameter =>
        {
            if (parameter is StimulationImpedanceDiagnosticChannelViewModel channel)
            {
                SelectedChannel = channel;
            }
        });
        hardwareService.StimulationImpedanceChanged += HardwareService_StimulationImpedanceChanged;
        hardwareService.ConnectionChanged += (_, _) => refreshCommand.RaiseCanExecuteChanged();
    }

    public ObservableCollection<StimulationImpedanceDiagnosticChannelViewModel> Channels { get; } = [];

    public ICommand RefreshCommand { get; }
    public ICommand SelectChannelCommand { get; }

    public StimulationImpedanceDiagnosticChannelViewModel? SelectedChannel
    {
        get => selectedChannel;
        private set => SetProperty(ref selectedChannel, value);
    }

    public string SummaryText
    {
        get => summaryText;
        private set => SetProperty(ref summaryText, value);
    }

    public string RefreshFeedbackText
    {
        get => refreshFeedbackText;
        private set => SetProperty(ref refreshFeedbackText, value);
    }

    public string ErrorText
    {
        get => errorText;
        private set => SetProperty(ref errorText, value);
    }

    public void LoadCurrentSnapshot()
    {
        RefreshFeedbackText = string.Empty;
        ErrorText = string.Empty;
        ApplySnapshot(hardwareService.CurrentStimulationImpedance);
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        RefreshFeedbackText = "正在读取阻抗…";
        ErrorText = string.Empty;
        await hardwareService.CheckImpedanceAsync(cancellationToken);
        ApplySnapshot(hardwareService.CurrentStimulationImpedance);
        RefreshFeedbackText = $"更新完成：{DateTime.Now:HH:mm:ss}";
    }

    private void HardwareService_StimulationImpedanceChanged(
        object? sender,
        StimulationImpedanceChangedEventArgs entry)
    {
        void Apply() => ApplySnapshot(entry.Snapshot);
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            Apply();
        }
        else
        {
            _ = dispatcher.InvokeAsync(Apply);
        }
    }

    private void ApplySnapshot(StimulationImpedanceSnapshot? snapshot)
    {
        var selectedNumber = SelectedChannel?.LogicalChannelNumber ?? 1;
        Channels.Clear();
        var source = snapshot?.Channels ?? CreateUnavailableChannels();
        foreach (var channel in source.OrderBy(channel => channel.LogicalChannelNumber))
        {
            Channels.Add(new StimulationImpedanceDiagnosticChannelViewModel(channel));
        }

        SelectedChannel = Channels.FirstOrDefault(channel => channel.LogicalChannelNumber == selectedNumber)
            ?? Channels.FirstOrDefault();
        var availableCount = snapshot?.Channels.Count(channel => channel.IsAvailable) ?? 0;
        SummaryText = snapshot is null
            ? "尚未取得阻抗数据；请点击“更新阻抗”。"
            : $"CH1～CH16 · 有效 {availableCount} · 最近快照 {snapshot.CapturedAt.ToLocalTime():HH:mm:ss}";
    }

    private void HandleRefreshError(Exception exception)
    {
        logger.Error("Debug阻抗诊断刷新失败", exception);
        RefreshFeedbackText = string.Empty;
        ErrorText = $"读取失败：{exception.Message}";
    }

    private static IReadOnlyList<StimulationImpedanceChannelSnapshot> CreateUnavailableChannels() =>
        Enumerable.Range(1, 16)
            .Select(number => new StimulationImpedanceChannelSnapshot(
                number, null, null, null, null, null, null, null))
            .ToArray();
}
