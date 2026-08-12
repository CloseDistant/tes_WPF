namespace RuinaoSoftwareWpf;

using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;

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
