using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;

namespace RuinaoSoftwareWpf;

/// <summary>
/// TI 控制页面 ViewModel。
///
/// 负责维护 TI 刺激组列表、当前选中组，以及开始/暂停/急停等页面级命令。
/// Shell 只负责展示该页面，不再持有 TI 控制页的业务状态。
/// </summary>
public sealed class TiControlViewModel : ObservableObject
{
    private readonly IStimulationEngine stimulationEngine;
    private readonly IHardwareConnectionState hardwareConnectionState;
    private readonly IDebugHardwareSimulationService debugHardwareSimulation;
    private readonly ILoggingService logger;
    private readonly IToastService toastService;
    private readonly StimulationChannelCountdown countdown = new();
    private readonly AsyncRelayCommand startCommand;
    private readonly AsyncRelayCommand startChannelCommand;
    private readonly RelayCommand usePrescriptionCommand;
    private readonly RelayCommand useChannelPrescriptionCommand;
    private TiGroup? selectedGroup;
    private TiGroup? lastSelectedGroup;
    private string appliedPrescriptionName = "手动设置";
    private string deliveryMode = PrescriptionDeliveryModes.Continuous;
    private int totalDurationMinutes = 20;
    private int? intervalMinutes;
    private int? sessionDurationMinutes;

    public TiControlViewModel(
        IStimulationEngine stimulationEngine,
        IHardwareConnectionState hardwareConnectionState,
        IDebugHardwareSimulationService debugHardwareSimulation,
        ILoggingService logger,
        ITiGroupFactory tiGroupFactory,
        LocalizationViewModel localization,
        IToastService toastService)
    {
        this.stimulationEngine = stimulationEngine;
        this.hardwareConnectionState = hardwareConnectionState;
        this.debugHardwareSimulation = debugHardwareSimulation;
        this.logger = logger;
        this.toastService = toastService;
        countdown.Completed += channel => _ = CompleteChannelAsync(channel);
        Localization = localization;
        Groups = new ObservableCollection<TiGroup>(tiGroupFactory.CreateDemoGroups());

        SelectGroupCommand = new RelayCommand(parameter =>
        {
            if (parameter is TiGroup group)
            {
                SelectedGroup = group;
            }
        });

        startCommand = new AsyncRelayCommand(
            (_, _) => StartSelectedGroupAsync(),
            _ => CanStartStimulation,
            HandleStartFailure);
        StartCommand = startCommand;
        startChannelCommand = new AsyncRelayCommand(
            async (parameter, _) =>
            {
                if (parameter is ChannelConfig channel)
                {
                    await StartChannelAsync(channel);
                }
            },
            _ => CanStartStimulation,
            onError: HandleStartFailure);
        StartChannelCommand = startChannelCommand;
        PauseCommand = CreateHardwareCommand(_ => PauseSelectedGroupAsync());
        EmergencyStopCommand = CreateHardwareCommand(_ => EmergencyStopSelectedGroupAsync());
        usePrescriptionCommand = new RelayCommand(
            _ => RequestPrescription(StimulationPrescriptionApplyScope.AllChannels),
            _ => !countdown.HasActiveChannels);
        UsePrescriptionCommand = usePrescriptionCommand;
        useChannelPrescriptionCommand = new RelayCommand(
            parameter => RequestPrescription(StimulationPrescriptionApplyScope.SingleChannel, parameter),
            parameter => parameter is ChannelConfig channel
                && Groups.SelectMany(group => group.Channels).Contains(channel)
                && !countdown.IsActive(channel));
        UseChannelPrescriptionCommand = useChannelPrescriptionCommand;
        BackCommand = new RelayCommand(_ => BackRequested?.Invoke(this, EventArgs.Empty));
        hardwareConnectionState.ConnectionChanged += OnHardwareConnectionChanged;
        debugHardwareSimulation.ConnectionChanged += OnDebugSimulationConnectionChanged;

        SelectedGroup = Groups.FirstOrDefault();
        lastSelectedGroup = SelectedGroup;
    }

    /// <summary>
    /// 硬件操作完成事件。
    /// MainViewModel 订阅它，用来刷新底部状态栏，避免 TI 页面直接持有 Shell。
    /// </summary>
    public event EventHandler<HardwareOperationResult>? HardwareOperationCompleted;

    /// <summary>请求返回电刺激类型选择页。</summary>
    public event EventHandler? BackRequested;

    public event EventHandler<StimulationPrescriptionRequestEventArgs>? PrescriptionRequested;

    public LocalizationViewModel Localization { get; }

    public ObservableCollection<TiGroup> Groups { get; }

    public ICommand SelectGroupCommand { get; }

    public ICommand StartCommand { get; }

    public ICommand StartChannelCommand { get; }

    public ICommand PauseCommand { get; }

    public ICommand EmergencyStopCommand { get; }

    public ICommand UsePrescriptionCommand { get; }

    public ICommand UseChannelPrescriptionCommand { get; }

    public ICommand BackCommand { get; }
    public string AppliedPrescriptionName { get => appliedPrescriptionName; private set => SetProperty(ref appliedPrescriptionName, value); }
    public string DeliveryMode { get => deliveryMode; private set => SetProperty(ref deliveryMode, value); }
    public int TotalDurationMinutes { get => totalDurationMinutes; private set => SetProperty(ref totalDurationMinutes, value); }
    public int? IntervalMinutes { get => intervalMinutes; private set => SetProperty(ref intervalMinutes, value); }
    public int? SessionDurationMinutes { get => sessionDurationMinutes; private set => SetProperty(ref sessionDurationMinutes, value); }

    public bool IsStimulationRunning => stimulationEngine.CurrentState == StimulationExecutionState.Running;

    public TiGroup? SelectedGroup
    {
        get => selectedGroup;
        set
        {
            if (SetProperty(ref selectedGroup, value))
            {
                foreach (var group in Groups)
                {
                    group.IsSelected = ReferenceEquals(group, value);
                }

                OnPropertyChanged(nameof(SelectedChannelNames));

                if (value is not null)
                {
                    lastSelectedGroup = value;
                    logger.Debug($"SELECT {value.Title} -> show only {SelectedChannelNames}");
                }
            }
        }
    }

    public string SelectedChannelNames =>
        SelectedGroup is null ? string.Empty : string.Join(" + ", SelectedGroup.Channels.Select(c => c.Name));

    /// <summary>
    /// 回到 TI 控制页时恢复上一次选择；如果没有历史选择，则默认选中第一组。
    /// </summary>
    public void RestoreLastSelection()
    {
        if (lastSelectedGroup is not null && Groups.Contains(lastSelectedGroup))
        {
            SelectedGroup = lastSelectedGroup;
            return;
        }

        SelectedGroup = Groups.FirstOrDefault();
    }

    public void ApplyPrescription(PrescriptionDefinition prescription)
    {
        ApplyPrescription(prescription, Groups.SelectMany(group => group.Channels));
    }

    public void ApplyPrescription(PrescriptionDefinition prescription, ChannelConfig channel)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ApplyPrescription(prescription, [channel]);
    }

    private void ApplyPrescription(
        PrescriptionDefinition prescription,
        IEnumerable<ChannelConfig> targetChannels)
    {
        ArgumentNullException.ThrowIfNull(prescription);
        AppliedPrescriptionName = prescription.Name;
        DeliveryMode = prescription.DeliveryMode;
        TotalDurationMinutes = prescription.TotalDurationMinutes;
        IntervalMinutes = prescription.IntervalMinutes;
        SessionDurationMinutes = prescription.SessionDurationMinutes;
        var current = prescription.CurrentMilliamp.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        var durationSeconds = (prescription.TotalDurationMinutes * 60).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var singleDurationSeconds = ((prescription.SessionDurationMinutes ?? prescription.TotalDurationMinutes) * 60)
            .ToString(System.Globalization.CultureInfo.InvariantCulture);
        var intervalSeconds = ((prescription.IntervalMinutes ?? 0) * 60)
            .ToString(System.Globalization.CultureInfo.InvariantCulture);
        var mode = prescription.DeliveryMode == PrescriptionDeliveryModes.Interval ? "间隔" : "连续";
        foreach (var channel in targetChannels)
        {
            // TI 处方不包含载波频率；处方应用不得覆盖通道自己的 FrequencyHz。
            channel.CurrentMA = current;
            channel.RampUpS = prescription.RampUpSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
            channel.RampDownS = prescription.RampDownSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
            channel.DurationS = durationSeconds;
            channel.IntervalS = intervalSeconds;
            channel.SingleDurationS = singleDurationSeconds;
            channel.StimulationMode = mode;
            channel.RemainingTime = "00:00:00";
            channel.DirectCurrentWaveform.Clear();
            channel.RefreshBindings();
        }

        OnPropertyChanged(nameof(SelectedGroup));
    }

    private void RequestPrescription(
        StimulationPrescriptionApplyScope scope,
        object? targetChannel = null)
    {
        PrescriptionRequested?.Invoke(
            this,
            new StimulationPrescriptionRequestEventArgs("TI", scope, targetChannel));
    }

    private ICommand CreateHardwareCommand(Func<object?, Task> execute, Action<Exception>? onError = null)
    {
        return new AsyncRelayCommand(
            async (parameter, _) => await execute(parameter),
            onError: onError ?? (ex => logger.Error("TI 控制命令执行失败", ex)));
    }

    private void HandleStartFailure(Exception exception)
    {
        logger.Error("刺激启动失败", exception);
        toastService.ShowError(
            "刺激启动失败",
            "刺激启动命令未完成，软件未进入运行状态。具体原因已记录到运行日志。");
    }

    private void OnHardwareConnectionChanged(
        object? sender,
        HardwareConnectionChangedEventArgs eventArgs)
    {
        RefreshStartCommandStatesOnUiThread();
    }

    private void OnDebugSimulationConnectionChanged(object? sender, EventArgs eventArgs)
    {
        RefreshStartCommandStatesOnUiThread();
    }

    private bool CanStartStimulation =>
        hardwareConnectionState.IsConnected || debugHardwareSimulation.IsConnected;

    private void RefreshStartCommandStatesOnUiThread()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            _ = dispatcher.BeginInvoke(RefreshStartCommandStates);
            return;
        }

        RefreshStartCommandStates();
    }

    private void RefreshStartCommandStates()
    {
        startCommand.RaiseCanExecuteChanged();
        startChannelCommand.RaiseCanExecuteChanged();
        usePrescriptionCommand.RaiseCanExecuteChanged();
        useChannelPrescriptionCommand.RaiseCanExecuteChanged();
    }

    private async Task StartSelectedGroupAsync()
    {
        if (SelectedGroup is null)
        {
            logger.Debug("PROTO START skipped: no TI group selected");
            return;
        }

        var result = await stimulationEngine.StartTiGroupAsync(
            SelectedGroup,
            SelectedChannelNames,
            AppliedPrescriptionName);
        foreach (var channel in SelectedGroup.Channels)
        {
            channel.IsParameterEditingEnabled = false;
            countdown.Start(channel);
        }
        RefreshStartCommandStates();

        HardwareOperationCompleted?.Invoke(this, result);
    }

    private async Task StartChannelAsync(ChannelConfig channel)
    {
        if (SelectedGroup is null || !SelectedGroup.Channels.Contains(channel))
        {
            logger.Debug("PROTO START channel skipped: channel is not in selected TI group");
            return;
        }

        var singleChannelGroup = new TiGroup
        {
            Title = SelectedGroup.Title
        };
        singleChannelGroup.Channels.Add(channel);

        var result = await stimulationEngine.StartTiGroupAsync(
            singleChannelGroup,
            channel.Name,
            AppliedPrescriptionName);
        countdown.Start(channel);
        channel.IsParameterEditingEnabled = false;
        RefreshStartCommandStates();
        HardwareOperationCompleted?.Invoke(this, result);
    }

    private async Task PauseSelectedGroupAsync()
    {
        if (SelectedGroup is null)
        {
            logger.Debug("PROTO PAUSE skipped: no TI group selected");
            return;
        }

        var result = await stimulationEngine.PauseTiGroupAsync(SelectedGroup, SelectedChannelNames);
        countdown.CancelAll(SelectedGroup.Channels, reset: false);
        foreach (var channel in SelectedGroup.Channels)
        {
            channel.IsParameterEditingEnabled = true;
        }
        RefreshStartCommandStates();
        HardwareOperationCompleted?.Invoke(this, result);
    }

    private async Task EmergencyStopSelectedGroupAsync()
    {
        if (SelectedGroup is null)
        {
            logger.Debug("PROTO EMERGENCY skipped: no TI group selected");
            return;
        }

        var result = await stimulationEngine.EmergencyStopTiGroupAsync(SelectedGroup, "用户点击急停");
        countdown.CancelAll(Groups.SelectMany(group => group.Channels), reset: true);
        foreach (var channel in Groups.SelectMany(group => group.Channels))
        {
            channel.IsParameterEditingEnabled = true;
        }
        RefreshStartCommandStates();
        HardwareOperationCompleted?.Invoke(this, result);
    }

    private async Task CompleteChannelAsync(ChannelConfig channel)
    {
        try
        {
            var owner = Groups.FirstOrDefault(group => group.Channels.Contains(channel));
            if (owner is null)
            {
                return;
            }

            var singleChannelGroup = new TiGroup { Title = owner.Title };
            singleChannelGroup.Channels.Add(channel);
            var result = await stimulationEngine.CompleteGroupAsync(
                singleChannelGroup,
                channel.Name,
                "TI");
            channel.IsParameterEditingEnabled = true;
            RefreshStartCommandStatesOnUiThread();
            HardwareOperationCompleted?.Invoke(this, result);
        }
        catch (Exception ex)
        {
            logger.Error($"TI 通道 {channel.Name} 完成记录失败", ex);
        }
    }
}
