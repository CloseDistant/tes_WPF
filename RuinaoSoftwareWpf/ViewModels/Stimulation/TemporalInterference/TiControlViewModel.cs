using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace RuinaoSoftwareWpf;

/// <summary>
/// TI 控制页面 ViewModel。
///
/// 负责维护 TI 刺激组列表、当前选中组，以及开始、停止和急停等页面级命令。
/// Shell 只负责展示该页面，不再持有 TI 控制页的业务状态。
/// </summary>
public sealed class TiControlViewModel : ObservableObject
{
    private readonly IStimulationEngine stimulationEngine;
    private readonly IHardwareConnectionState hardwareConnectionState;
    private readonly IHardwareService? hardwareService;
    private readonly IDebugHardwareSimulationService debugHardwareSimulation;
    private readonly IDebugStimulationImpedanceProvider? debugImpedanceProvider;
    private readonly ITiWaveformPreviewFactory waveformPreviewFactory;
    private readonly ILoggingService logger;
    private readonly IToastService toastService;
    private readonly IUserDialogService userDialogService;
    private readonly StimulationChannelCountdown countdown = new();
    private readonly DispatcherTimer waveformTimer;
    private readonly Dictionary<ChannelConfig, TiWaveformRuntime> activeWaveforms = [];
    private readonly AsyncRelayCommand startCommand;
    private readonly AsyncRelayCommand startChannelCommand;
    private readonly AsyncRelayCommand stopChannelCommand;
    private readonly RelayCommand usePrescriptionCommand;
    private readonly RelayCommand useChannelPrescriptionCommand;
    private readonly RelayCommand backCommand;
    private bool startOperationInProgress;
    private bool isParameterDownloadVisible;
    private double parameterDownloadPercentage;
    private string parameterDownloadStatus = "正在准备刺激参数";
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
        IToastService toastService,
        IUserDialogService userDialogService,
        IDebugStimulationImpedanceProvider? debugImpedanceProvider = null,
        ITiWaveformPreviewFactory? waveformPreviewFactory = null)
    {
        this.stimulationEngine = stimulationEngine;
        this.hardwareConnectionState = hardwareConnectionState;
        hardwareService = hardwareConnectionState as IHardwareService;
        this.debugHardwareSimulation = debugHardwareSimulation;
        this.debugImpedanceProvider = debugImpedanceProvider;
        this.waveformPreviewFactory = waveformPreviewFactory ?? new TiWaveformPreviewFactory();
        this.logger = logger;
        this.toastService = toastService;
        this.userDialogService = userDialogService;
        countdown.Completed += channel => _ = CompleteChannelAsync(channel);
        waveformTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(50)
        };
        waveformTimer.Tick += OnWaveformTimerTick;
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
            (_, cancellationToken) => StartAllChannelsAsync(cancellationToken),
            _ => CanStartStimulation
                && !countdown.HasActiveChannels
                && Groups.SelectMany(group => group.Channels).Any(IsImpedanceEligibleForStart),
            HandleStartFailure);
        StartCommand = startCommand;
        startChannelCommand = new AsyncRelayCommand(
            async (parameter, cancellationToken) =>
            {
                if (parameter is ChannelConfig channel)
                {
                    await StartChannelAsync(channel, cancellationToken);
                }
            },
            parameter => CanStartStimulation
                && parameter is ChannelConfig channel
                && !countdown.IsActive(channel)
                && IsImpedanceEligibleForStart(channel),
            onError: HandleStartFailure);
        StartChannelCommand = startChannelCommand;
        stopChannelCommand = new AsyncRelayCommand(
            async (parameter, cancellationToken) =>
            {
                if (parameter is ChannelConfig channel)
                {
                    try
                    {
                        await StopChannelAsync(channel, cancellationToken);
                    }
                    catch (Exception exception)
                    {
                        HandleStopFailure(channel, exception);
                    }
                }
            },
            parameter => CanControlHardware
                && parameter is ChannelConfig channel
                && countdown.IsActive(channel));
        StopChannelCommand = stopChannelCommand;
        EmergencyStopCommand = CreateHardwareCommand(_ => EmergencyStopAllChannelsAsync());
        usePrescriptionCommand = new RelayCommand(
            _ => RequestPrescription(StimulationPrescriptionApplyScope.AllChannels),
            _ => !countdown.HasActiveChannels && !startOperationInProgress);
        UsePrescriptionCommand = usePrescriptionCommand;
        useChannelPrescriptionCommand = new RelayCommand(
            parameter => RequestPrescription(StimulationPrescriptionApplyScope.SingleChannel, parameter),
            parameter => parameter is ChannelConfig channel
                && Groups.SelectMany(group => group.Channels).Contains(channel)
                && !countdown.IsActive(channel)
                && !startOperationInProgress);
        UseChannelPrescriptionCommand = useChannelPrescriptionCommand;
        ParameterValidationFailedCommand = new RelayCommand(parameter =>
        {
            if (parameter is string message && !string.IsNullOrWhiteSpace(message))
            {
                toastService.Show(ToastKind.Warning, "参数已调整", message);
            }
        });
        backCommand = new RelayCommand(
            _ => BackRequested?.Invoke(this, EventArgs.Empty),
            _ => !HasConfirmedRunningChannels());
        BackCommand = backCommand;
        hardwareConnectionState.ConnectionChanged += OnHardwareConnectionChanged;
        if (hardwareService is not null)
        {
            hardwareService.StimulationImpedanceChanged += OnStimulationImpedanceChanged;
        }
        debugHardwareSimulation.ConnectionChanged += OnDebugSimulationConnectionChanged;

        SelectedGroup = Groups.FirstOrDefault();
        lastSelectedGroup = SelectedGroup;
        ApplyDebugImpedanceSnapshotIfAvailable();
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

    public ICommand StopChannelCommand { get; }

    public ICommand EmergencyStopCommand { get; }

    public ICommand UsePrescriptionCommand { get; }

    public ICommand UseChannelPrescriptionCommand { get; }

    public ICommand BackCommand { get; }

    public ICommand ParameterValidationFailedCommand { get; }
    public string AppliedPrescriptionName { get => appliedPrescriptionName; private set => SetProperty(ref appliedPrescriptionName, value); }
    public string DeliveryMode { get => deliveryMode; private set => SetProperty(ref deliveryMode, value); }
    public int TotalDurationMinutes { get => totalDurationMinutes; private set => SetProperty(ref totalDurationMinutes, value); }
    public int? IntervalMinutes { get => intervalMinutes; private set => SetProperty(ref intervalMinutes, value); }
    public int? SessionDurationMinutes { get => sessionDurationMinutes; private set => SetProperty(ref sessionDurationMinutes, value); }

    public bool IsStimulationRunning => stimulationEngine.CurrentState == StimulationExecutionState.Running;

    public bool IsParameterDownloadVisible
    {
        get => isParameterDownloadVisible;
        private set => SetProperty(ref isParameterDownloadVisible, value);
    }

    public double ParameterDownloadPercentage
    {
        get => parameterDownloadPercentage;
        private set
        {
            if (SetProperty(ref parameterDownloadPercentage, value))
            {
                OnPropertyChanged(nameof(ParameterDownloadPercentageText));
            }
        }
    }

    public string ParameterDownloadPercentageText => $"{ParameterDownloadPercentage:0}%";

    public string ParameterDownloadStatus
    {
        get => parameterDownloadStatus;
        private set => SetProperty(ref parameterDownloadStatus, value);
    }

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
        DeliveryMode = PrescriptionDeliveryModes.Continuous;
        TotalDurationMinutes = prescription.TotalDurationMinutes;
        IntervalMinutes = null;
        SessionDurationMinutes = null;
        var current = prescription.CurrentMilliamp.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture);
        var durationSeconds = prescription.DirectCurrentTotalDurationSeconds
            .ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        var rampUpSeconds = prescription.DirectCurrentRampUpDurationSeconds
            .ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        var rampDownSeconds = prescription.DirectCurrentRampDownDurationSeconds
            .ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        foreach (var channel in targetChannels)
        {
            // TI 处方不包含载波频率；处方应用不得覆盖通道自己的 FrequencyHz。
            channel.CurrentMA = current;
            channel.RampUpS = rampUpSeconds;
            channel.RampDownS = rampDownSeconds;
            channel.DurationS = durationSeconds;
            channel.IntervalS = "0.0";
            channel.SingleDurationS = durationSeconds;
            channel.StimulationMode = "连续";
            channel.RemainingTime = "00:00:00";
            channel.DirectCurrentWaveform.Clear();
            channel.AlternatingCurrentWaveform.Clear();
            activeWaveforms.Remove(channel);
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
            new StimulationPrescriptionRequestEventArgs(
                StimulationModeCodes.TemporalInterference,
                scope,
                targetChannel));
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
            $"刺激启动命令未完成，软件未进入运行状态。{exception.Message}");
    }

    private void HandleStopFailure(ChannelConfig channel, Exception exception)
    {
        RefreshStartCommandStates();
        logger.Error($"TI 刺激停止失败：{channel.Name}", exception);
        toastService.ShowError(
            "刺激停止失败",
            $"{channel.Name}停止命令未确认，通道状态未知。{exception.Message}");
        if (userDialogService.ConfirmWarning(
                "停止失败",
                $"{channel.Name}停止未得到有效确认，是否立即执行紧急停止？",
                "紧急停止",
                "暂不处理")
            && EmergencyStopCommand.CanExecute(null))
        {
            EmergencyStopCommand.Execute(null);
        }
    }

    private void OnHardwareConnectionChanged(
        object? sender,
        HardwareConnectionChangedEventArgs eventArgs)
    {
        if (!eventArgs.IsConnected)
        {
            foreach (var channel in Groups.SelectMany(group => group.Channels).Where(countdown.IsActive))
            {
                channel.IsStateUnknown = true;
            }
        }

        RefreshStartCommandStatesOnUiThread();
    }

    private void OnDebugSimulationConnectionChanged(object? sender, EventArgs eventArgs)
    {
        ApplyDebugImpedanceSnapshotIfAvailable();
        RefreshStartCommandStatesOnUiThread();
    }

    private bool CanStartStimulation =>
        CanControlHardware && !startOperationInProgress;

    private bool CanControlHardware =>
        hardwareConnectionState.IsConnected || debugHardwareSimulation.IsConnected;

    private static bool IsImpedanceEligibleForStart(ChannelConfig channel) =>
        channel.ImpedanceStatus is
            StimulationImpedanceStatus.Normal or StimulationImpedanceStatus.Warning;

    private bool HasConfirmedRunningChannels() =>
        Groups.SelectMany(group => group.Channels)
            .Any(channel => countdown.IsActive(channel) && !channel.IsStateUnknown);

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
        stopChannelCommand.RaiseCanExecuteChanged();
        usePrescriptionCommand.RaiseCanExecuteChanged();
        useChannelPrescriptionCommand.RaiseCanExecuteChanged();
        backCommand.RaiseCanExecuteChanged();
    }

    private async Task EnsureFreshImpedanceAsync(CancellationToken cancellationToken)
    {
        if (!hardwareConnectionState.IsConnected || hardwareService is null)
        {
            return;
        }

        var snapshot = hardwareService.CurrentStimulationImpedance;
        if (snapshot is not null
            && DateTimeOffset.Now - snapshot.CapturedAt <= TimeSpan.FromSeconds(10))
        {
            return;
        }

        _ = await hardwareService.CheckImpedanceAsync(cancellationToken);
        ApplyImpedanceSnapshot(hardwareService.CurrentStimulationImpedance);
    }

    private void OnStimulationImpedanceChanged(
        object? sender,
        StimulationImpedanceChangedEventArgs eventArgs)
    {
        void Apply()
        {
            if (!ApplyDebugImpedanceSnapshotIfAvailable())
            {
                ApplyImpedanceSnapshot(eventArgs.Snapshot);
            }
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            _ = dispatcher.BeginInvoke(Apply);
            return;
        }

        Apply();
    }

    private bool ApplyDebugImpedanceSnapshotIfAvailable()
    {
        if (!debugHardwareSimulation.IsConnected
            || debugImpedanceProvider?.GetSnapshot() is not { } snapshot)
        {
            return false;
        }

        ApplyImpedanceSnapshot(snapshot);
        return true;
    }

    private void ApplyImpedanceSnapshot(StimulationImpedanceSnapshot? snapshot)
    {
        var channels = Groups.SelectMany(group => group.Channels).ToArray();
        var values = snapshot?.Channels.ToDictionary(
            channel => channel.LogicalChannelNumber,
            channel => channel.ImpedanceOhms);
        for (var index = 0; index < channels.Length; index++)
        {
            channels[index].UpdateImpedance(values?.GetValueOrDefault(index + 1));
        }

        RefreshStartCommandStates();
    }

    private static bool TryValidateChannels(
        IEnumerable<ChannelConfig> channels,
        out string error)
    {
        foreach (var channel in channels)
        {
            if (!TiAlternatingCurrentParameters.TryCreate(channel, out _, out error))
            {
                return false;
            }
        }

        error = string.Empty;
        return true;
    }

    private void SetStartOperationState(
        IEnumerable<ChannelConfig> channels,
        bool isStarting)
    {
        startOperationInProgress = isStarting;
        foreach (var channel in channels)
        {
            channel.IsStarting = isStarting;
            if (isStarting)
            {
                channel.IsParameterEditingEnabled = false;
            }
        }

        RefreshStartCommandStates();
    }

    private void ShowParameterDownloadProgress()
    {
        ParameterDownloadPercentage = 0;
        ParameterDownloadStatus = "正在准备刺激参数";
        IsParameterDownloadVisible = true;
    }

    private void UpdateParameterDownloadProgress(StimulationParameterDownloadProgress progress)
    {
        ParameterDownloadPercentage = progress.Percentage;
        ParameterDownloadStatus = progress.TotalCommandCount > 0
            && progress.CompletedCommandCount >= progress.TotalCommandCount
                ? "参数下发完成，正在同步开始刺激"
                : "正在下发刺激参数";
    }

    private void HideParameterDownloadProgress()
    {
        IsParameterDownloadVisible = false;
    }

    private async Task StartAllChannelsAsync(CancellationToken cancellationToken)
    {
        if (countdown.HasActiveChannels)
        {
            toastService.ShowError("同步开始失败", "已有通道处于刺激中，不能再次执行同步开始。");
            return;
        }

        await EnsureFreshImpedanceAsync(cancellationToken);
        var allChannels = Groups
            .SelectMany(group => group.Channels)
            .ToArray();
        if (allChannels.Length != 16)
        {
            toastService.ShowError("同步开始失败", "同步开始要求 16 个通道全部可用。");
            return;
        }

        var synchronizedChannels = allChannels
            .Where(IsImpedanceEligibleForStart)
            .ToArray();
        if (synchronizedChannels.Length == 0)
        {
            toastService.ShowError("同步开始失败", "没有阻抗状态允许启动的通道。");
            return;
        }

        var excludedCount = allChannels.Length - synchronizedChannels.Length;
        var confirmationMessage = excludedCount == 0
            ? "系统将检查全部16个通道，并在参数下发完成后同步开始刺激。"
            : $"系统将检查全部16个通道；其中{excludedCount}个通道因阻抗状态不可用，本次将启动其余{synchronizedChannels.Length}个通道。";
        if (!userDialogService.ConfirmWarning(
                "同步开始确认",
                confirmationMessage,
                "确认同步开始",
                "取消"))
        {
            return;
        }

        if (!TryValidateChannels(synchronizedChannels, out var validationError))
        {
            toastService.ShowError("参数校验失败", validationError);
            return;
        }

        var result = await StartChannelsAsync(
            "TI 全通道同步刺激",
            synchronizedChannels,
            cancellationToken);
        HardwareOperationCompleted?.Invoke(this, result);
    }

    private async Task StartChannelAsync(
        ChannelConfig channel,
        CancellationToken cancellationToken)
    {
        if (SelectedGroup is null || !SelectedGroup.Channels.Contains(channel))
        {
            logger.Debug("PROTO START channel skipped: channel is not in selected TI group");
            return;
        }

        await EnsureFreshImpedanceAsync(cancellationToken);
        if (!IsImpedanceEligibleForStart(channel))
        {
            toastService.ShowError("开始刺激失败", $"{channel.Name}当前阻抗状态不允许开始刺激。");
            return;
        }

        if (!TiAlternatingCurrentParameters.TryCreate(channel, out _, out var error))
        {
            toastService.ShowError("参数校验失败", error);
            return;
        }

        if (!userDialogService.ConfirmWarning(
                "开始刺激确认",
                $"即将开始 {channel.Name} 的刺激，是否确认继续？",
                "确认开始",
                "返回修改"))
        {
            return;
        }

        var result = await StartChannelsAsync(
            SelectedGroup.Title,
            [channel],
            cancellationToken);
        HardwareOperationCompleted?.Invoke(this, result);
    }

    private async Task<HardwareOperationResult> StartChannelsAsync(
        string title,
        IReadOnlyList<ChannelConfig> channels,
        CancellationToken cancellationToken)
    {
        if (!TryValidateChannels(channels, out var validationError))
        {
            throw new InvalidOperationException(validationError);
        }

        var waveformPreviews = CreateWaveformPreviews(channels);

        var executionGroup = CreateExecutionGroup(title, channels);
        var started = false;
        SetStartOperationState(channels, true);
        ShowParameterDownloadProgress();
        try
        {
            var progress = new Progress<StimulationParameterDownloadProgress>(UpdateParameterDownloadProgress);
            var result = await stimulationEngine.StartTiGroupAsync(
                executionGroup,
                string.Join(" + ", channels.Select(channel => channel.Name)),
                AppliedPrescriptionName,
                progress,
                cancellationToken);
            var startTimestamp = Stopwatch.GetTimestamp();
            foreach (var channel in channels)
            {
                channel.IsStateUnknown = false;
                channel.IsParameterEditingEnabled = false;
                channel.IsStimulating = true;
                channel.AlternatingCurrentWaveform.Start(waveformPreviews[channel]);
                activeWaveforms[channel] = new TiWaveformRuntime(
                    startTimestamp,
                    waveformPreviews[channel]);
                countdown.Start(channel);
            }

            if (!waveformTimer.IsEnabled)
            {
                waveformTimer.Start();
            }

            started = true;
            return result;
        }
        finally
        {
            HideParameterDownloadProgress();
            SetStartOperationState(channels, false);
            if (!started)
            {
                foreach (var channel in channels.Where(channel => !countdown.IsActive(channel)))
                {
                    channel.IsParameterEditingEnabled = true;
                }
            }
        }
    }

    private async Task StopChannelAsync(
        ChannelConfig channel,
        CancellationToken cancellationToken)
    {
        if (!CanControlHardware || !countdown.IsActive(channel))
        {
            return;
        }

        if (!userDialogService.ConfirmWarning(
                "停止刺激确认",
                $"即将停止 {channel.Name}。",
                "确认停止",
                "取消"))
        {
            return;
        }

        var owner = Groups.FirstOrDefault(group => group.Channels.Contains(channel));
        if (owner is null)
        {
            return;
        }

        var group = new TiGroup { Title = owner.Title };
        group.Channels.Add(channel);
        try
        {
            var result = await stimulationEngine.StopGroupAsync(
                group,
                channel.Name,
                StimulationModeCodes.TemporalInterference,
                cancellationToken);
            countdown.Cancel(channel, reset: true);
            StopWaveform(channel, completed: false);
            channel.IsParameterEditingEnabled = true;
            channel.IsStimulating = false;
            channel.IsStateUnknown = false;
            RefreshStartCommandStates();
            logger.Info($"TI通道手动停止成功：{channel.Name}");
            HardwareOperationCompleted?.Invoke(this, result);
        }
        catch
        {
            channel.IsStateUnknown = true;
            throw;
        }
    }

    private async Task EmergencyStopAllChannelsAsync()
    {
        var runningChannels = Groups
            .SelectMany(group => group.Channels)
            .Where(countdown.IsActive)
            .ToArray();
        if (runningChannels.Length == 0)
        {
            logger.Debug("PROTO EMERGENCY skipped: no active TI channels");
            return;
        }

        var runningGroup = CreateExecutionGroup("TI 运行通道", runningChannels);
        var result = await stimulationEngine.EmergencyStopTiGroupAsync(runningGroup, "用户点击急停");
        countdown.CancelAll(Groups.SelectMany(group => group.Channels), reset: true);
        foreach (var channel in Groups.SelectMany(group => group.Channels))
        {
            StopWaveform(channel, completed: false);
            channel.IsParameterEditingEnabled = true;
            channel.IsStimulating = false;
            channel.IsStateUnknown = false;
        }
        RefreshStartCommandStates();
        HardwareOperationCompleted?.Invoke(this, result);
    }

    private static TiGroup CreateExecutionGroup(
        string title,
        IEnumerable<ChannelConfig> channels)
    {
        var group = new TiGroup { Title = title };
        foreach (var channel in channels)
        {
            group.Channels.Add(channel);
        }

        return group;
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
                StimulationModeCodes.TemporalInterference);
            StopWaveform(channel, completed: true);
            channel.IsParameterEditingEnabled = true;
            channel.IsStimulating = false;
            channel.IsStateUnknown = false;
            RefreshStartCommandStatesOnUiThread();
            HardwareOperationCompleted?.Invoke(this, result);
        }
        catch (Exception ex)
        {
            logger.Error($"TI 通道 {channel.Name} 完成记录失败", ex);
        }
    }

    private Dictionary<ChannelConfig, AlternatingCurrentWaveformPreview> CreateWaveformPreviews(
        IEnumerable<ChannelConfig> channels)
    {
        var result = new Dictionary<ChannelConfig, AlternatingCurrentWaveformPreview>();
        foreach (var channel in channels)
        {
            if (!TiAlternatingCurrentParameters.TryCreate(channel, out var parameters, out var error)
                || parameters is null)
            {
                throw new InvalidOperationException(error);
            }

            result[channel] = waveformPreviewFactory.Create(parameters);
        }

        return result;
    }

    private void OnWaveformTimerTick(object? sender, EventArgs eventArgs)
    {
        if (activeWaveforms.Count == 0)
        {
            waveformTimer.Stop();
            return;
        }

        var now = Stopwatch.GetTimestamp();
        foreach (var pair in activeWaveforms.ToArray())
        {
            var elapsed = Stopwatch.GetElapsedTime(pair.Value.StartTimestamp, now).TotalSeconds;
            pair.Key.AlternatingCurrentWaveform.UpdateElapsed(elapsed);
            if (elapsed < pair.Value.Preview.TotalDurationSeconds)
            {
                continue;
            }

            pair.Key.AlternatingCurrentWaveform.Complete();
            activeWaveforms.Remove(pair.Key);
        }

        if (activeWaveforms.Count == 0)
        {
            waveformTimer.Stop();
        }
    }

    private void StopWaveform(ChannelConfig channel, bool completed)
    {
        var elapsedSeconds = channel.AlternatingCurrentWaveform.ElapsedSeconds;
        if (activeWaveforms.Remove(channel, out var runtime))
        {
            elapsedSeconds = Stopwatch.GetElapsedTime(
                runtime.StartTimestamp,
                Stopwatch.GetTimestamp()).TotalSeconds;
        }

        if (completed)
        {
            channel.AlternatingCurrentWaveform.Complete();
        }
        else
        {
            channel.AlternatingCurrentWaveform.Stop(elapsedSeconds);
        }

        if (activeWaveforms.Count == 0)
        {
            waveformTimer.Stop();
        }
    }

    private sealed record TiWaveformRuntime(
        long StartTimestamp,
        AlternatingCurrentWaveformPreview Preview);
}
