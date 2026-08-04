using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows.Input;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace RuinaoSoftwareWpf;

/// <summary>经颅直流电刺激页面状态和通道级控制命令。</summary>
public sealed class DirectCurrentControlViewModel : ObservableObject
{
    private static readonly TimeSpan EmergencyStopCooldown = TimeSpan.FromSeconds(2);

    private readonly IStimulationEngine stimulationEngine;
    private readonly IHardwareConnectionState hardwareConnectionState;
    private readonly IHardwareService? hardwareService;
    private readonly IDebugHardwareSimulationService debugHardwareSimulation;
    private readonly IDebugStimulationImpedanceProvider? debugImpedanceProvider;
    private readonly ILoggingService logger;
    private readonly IToastService toastService;
    private readonly IUserDialogService userDialogService;
    private readonly DispatcherTimer waveformTimer;
    private readonly Dictionary<ChannelConfig, ChannelRuntime> activeChannels = [];
    private readonly HashSet<ChannelConfig> completionPendingChannels = [];
    private readonly HashSet<ChannelConfig> completionStopFailedChannels = [];
    private readonly HashSet<ChannelConfig> impedanceStopPendingChannels = [];
    private readonly Dictionary<ChannelConfig, StimulationImpedanceStatus> previousImpedanceStatuses = [];
    private readonly AsyncRelayCommand synchronizedStartCommand;
    private readonly AsyncRelayCommand startChannelCommand;
    private readonly AsyncRelayCommand stopChannelCommand;
    private readonly AsyncRelayCommand emergencyStopCommand;
    private readonly RelayCommand usePrescriptionCommand;
    private readonly RelayCommand useChannelPrescriptionCommand;
    private string appliedPrescriptionName = "手动设置";
    private DirectCurrentChannelPair? selectedChannelPair;
    private ChannelConfig? selectedChannel;
    private bool startOperationInProgress;
    private bool emergencyStopCooldownInProgress;

    public DirectCurrentControlViewModel(
        IStimulationEngine stimulationEngine,
        IHardwareConnectionState hardwareConnectionState,
        IDebugHardwareSimulationService debugHardwareSimulation,
        ILoggingService logger,
        LocalizationViewModel localization,
        IToastService toastService,
        IUserDialogService userDialogService,
        IDebugStimulationImpedanceProvider? debugImpedanceProvider = null)
    {
        this.stimulationEngine = stimulationEngine;
        this.hardwareConnectionState = hardwareConnectionState;
        hardwareService = hardwareConnectionState as IHardwareService;
        this.debugHardwareSimulation = debugHardwareSimulation;
        this.debugImpedanceProvider = debugImpedanceProvider;
        this.logger = logger;
        this.toastService = toastService;
        this.userDialogService = userDialogService;
        Localization = localization;

        var accent = new SolidColorBrush(Color.FromRgb(228, 232, 239));
        accent.Freeze();
        Channels = new ObservableCollection<ChannelConfig>(
            Enumerable.Range(1, 16).Select(channelNumber =>
                CreateChannel($"CH {channelNumber}", accent)));
        foreach (var channel in Channels)
        {
            previousImpedanceStatuses[channel] = channel.ImpedanceStatus;
        }
        ChannelPairs = new ObservableCollection<DirectCurrentChannelPair>(
            Enumerable.Range(0, 8).Select(pairIndex =>
                new DirectCurrentChannelPair(
                    pairIndex + 1,
                    Channels[pairIndex * 2],
                    Channels[pairIndex * 2 + 1])));

        waveformTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        waveformTimer.Tick += OnWaveformTimerTick;

        BackCommand = new RelayCommand(_ => RequestBack());
        SelectChannelCommand = new RelayCommand(parameter =>
        {
            if (parameter is ChannelConfig channel && Channels.Contains(channel))
            {
                SelectedChannelPair = ChannelPairs.First(pair => pair.Channels.Contains(channel));
                SelectedChannel = channel;
            }
        });
        synchronizedStartCommand = new AsyncRelayCommand(
            (_, cancellationToken) => StartSynchronizedAsync(cancellationToken),
            _ => CanStartStimulation
                && activeChannels.Count == 0
                && Channels.Any(IsChannelEligibleToStart),
            HandleStartFailure);
        SynchronizedStartCommand = synchronizedStartCommand;
        startChannelCommand = new AsyncRelayCommand(
            async (parameter, cancellationToken) =>
            {
                if (parameter is ChannelConfig channel)
                {
                    await StartChannelAsync(channel, cancellationToken);
                }
            }, parameter => CanStartStimulation
                && parameter is ChannelConfig channel
                && IsChannelEligibleToStart(channel)
                && !activeChannels.ContainsKey(channel),
            onError: HandleStartFailure);
        StartChannelCommand = startChannelCommand;
        stopChannelCommand = new AsyncRelayCommand(
            async (parameter, cancellationToken) =>
            {
                if (parameter is ChannelConfig channel)
                {
                    await StopChannelAsync(channel, cancellationToken);
                }
            },
            parameter => CanControlHardware
                && !emergencyStopCooldownInProgress
                && parameter is ChannelConfig channel
                && activeChannels.ContainsKey(channel),
            onError: HandleStopFailure);
        StopChannelCommand = stopChannelCommand;
        emergencyStopCommand = new AsyncRelayCommand(
            (_, cancellationToken) => EmergencyStopAsync(cancellationToken),
            _ => CanControlHardware && !emergencyStopCooldownInProgress,
            HandleEmergencyStopFailure);
        EmergencyStopCommand = emergencyStopCommand;
        usePrescriptionCommand = new RelayCommand(
            _ => RequestPrescription(StimulationPrescriptionApplyScope.AllChannels),
            _ => activeChannels.Count == 0
                && !startOperationInProgress
                && !emergencyStopCooldownInProgress);
        UsePrescriptionCommand = usePrescriptionCommand;
        useChannelPrescriptionCommand = new RelayCommand(
            parameter => RequestPrescription(StimulationPrescriptionApplyScope.SingleChannel, parameter),
            parameter => parameter is ChannelConfig channel
                && Channels.Contains(channel)
                && !activeChannels.ContainsKey(channel)
                && !startOperationInProgress
                && !emergencyStopCooldownInProgress);
        UseChannelPrescriptionCommand = useChannelPrescriptionCommand;
        ParameterValidationFailedCommand = new RelayCommand(parameter =>
        {
            if (parameter is string message && !string.IsNullOrWhiteSpace(message))
            {
                toastService.Show(ToastKind.Warning, "参数已调整", message);
            }
        });
        hardwareConnectionState.ConnectionChanged += OnHardwareConnectionChanged;
        if (hardwareService is not null)
        {
            hardwareService.StimulationImpedanceChanged += OnStimulationImpedanceChanged;
        }
        debugHardwareSimulation.ConnectionChanged += OnDebugSimulationConnectionChanged;
        SelectedChannelPair = ChannelPairs[0];
        SelectedChannel = Channels[0];
        ApplyDebugImpedanceSnapshotIfAvailable();
    }

    public LocalizationViewModel Localization { get; }

    public ObservableCollection<ChannelConfig> Channels { get; }

    public ObservableCollection<DirectCurrentChannelPair> ChannelPairs { get; }

    public DirectCurrentChannelPair? SelectedChannelPair
    {
        get => selectedChannelPair;
        private set
        {
            if (!SetProperty(ref selectedChannelPair, value))
            {
                return;
            }

            OnPropertyChanged(nameof(SelectedChannels));
        }
    }

    public ChannelConfig? SelectedChannel
    {
        get => selectedChannel;
        private set
        {
            if (!SetProperty(ref selectedChannel, value))
            {
                return;
            }

            foreach (var channel in Channels)
            {
                channel.IsSelected = ReferenceEquals(channel, value);
            }
        }
    }

    public IReadOnlyList<ChannelConfig> SelectedChannels =>
        SelectedChannelPair?.Channels ?? Array.Empty<ChannelConfig>();

    public ICommand BackCommand { get; }

    public ICommand SelectChannelCommand { get; }

    public ICommand SynchronizedStartCommand { get; }

    public ICommand StartChannelCommand { get; }

    public ICommand StopChannelCommand { get; }

    public ICommand EmergencyStopCommand { get; }

    public ICommand UsePrescriptionCommand { get; }

    public ICommand UseChannelPrescriptionCommand { get; }

    public ICommand ParameterValidationFailedCommand { get; }

    public string AppliedPrescriptionName
    {
        get => appliedPrescriptionName;
        private set => SetProperty(ref appliedPrescriptionName, value);
    }

    public event EventHandler? BackRequested;

    public event EventHandler<HardwareOperationResult>? HardwareOperationCompleted;

    public event EventHandler<StimulationPrescriptionRequestEventArgs>? PrescriptionRequested;

    public void ApplyPrescription(PrescriptionDefinition prescription)
    {
        ApplyPrescription(prescription, Channels);
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
        var current = DirectCurrentParameterRules.FormatCurrent(prescription.CurrentMilliamp);
        var duration = DirectCurrentParameterRules.FormatTime(prescription.DirectCurrentTotalDurationSeconds);
        var interval = DirectCurrentParameterRules.FormatTime(prescription.DirectCurrentIntervalDurationSeconds);
        var singleDuration = DirectCurrentParameterRules.FormatTime(prescription.DirectCurrentSingleDurationSeconds);
        var mode = prescription.DeliveryMode == PrescriptionDeliveryModes.Interval ? "间隔" : "连续";

        foreach (var channel in targetChannels)
        {
            channel.CurrentMA = current;
            channel.RampUpS = DirectCurrentParameterRules.FormatTime(prescription.DirectCurrentRampUpDurationSeconds);
            channel.RampDownS = DirectCurrentParameterRules.FormatTime(prescription.DirectCurrentRampDownDurationSeconds);
            channel.DurationS = duration;
            channel.IntervalS = interval;
            channel.SingleDurationS = singleDuration;
            channel.StimulationMode = mode;
            channel.RemainingTime = "00:00:00";
            channel.DirectCurrentWaveform.Clear();
            channel.RefreshBindings();
        }

        OnPropertyChanged(nameof(SelectedChannels));
    }

    private void RequestPrescription(
        StimulationPrescriptionApplyScope scope,
        object? targetChannel = null)
    {
        PrescriptionRequested?.Invoke(
            this,
            new StimulationPrescriptionRequestEventArgs("tDCS", scope, targetChannel));
    }

    private static ChannelConfig CreateChannel(string name, Brush accent)
    {
        return new ChannelConfig
        {
            Name = name,
            CurrentMA = DirectCurrentParameterRules.DefaultCurrentMilliamp,
            RampUpS = DirectCurrentParameterRules.DefaultRampUpSeconds,
            RampDownS = DirectCurrentParameterRules.DefaultRampDownSeconds,
            DurationS = DirectCurrentParameterRules.DefaultTotalDurationSeconds,
            IntervalS = DirectCurrentParameterRules.DefaultIntervalSeconds,
            SingleDurationS = DirectCurrentParameterRules.DefaultSingleDurationSeconds,
            FrequencyHz = string.Empty,
            Polarity = "不掉转",
            StimulationMode = "间隔",
            AccentBrush = accent
        };
    }

    private ICommand CreateHardwareCommand(Func<object?, Task> execute, Action<Exception>? onError = null)
    {
        return new AsyncRelayCommand(
            async (parameter, _) => await execute(parameter),
            onError: onError ?? (ex => logger.Error("tDCS 控制命令执行失败", ex)));
    }

    private void HandleStartFailure(Exception exception)
    {
        logger.Error("刺激启动失败", exception);
        toastService.ShowError(
            "刺激启动失败",
            "刺激启动命令未完成，软件未进入运行状态。具体原因已记录到运行日志。");
    }

    private void HandleStopFailure(Exception exception)
    {
        logger.Error("tDCS 刺激停止失败", exception);
        toastService.ShowError(
            "刺激停止失败",
            "停止命令未完成，通道仍保持运行状态，请再次点击停止或使用紧急停止。具体原因已记录到运行日志。");
    }

    private void HandleEmergencyStopFailure(Exception exception)
    {
        logger.Error("tDCS 背板急停命令执行失败", exception);
        toastService.ShowError(
            "紧急停止失败",
            "背板紧急停止命令未确认，请立即人工检查设备并再次尝试急停。");
    }

    private async Task StartSynchronizedAsync(CancellationToken cancellationToken)
    {
        if (activeChannels.Count > 0)
        {
            toastService.ShowInformation("已有通道正在运行，不能执行同步开始。", "同步开始");
            return;
        }

        await EnsureFreshImpedanceAsync(cancellationToken);
        var impedanceAssessment = StimulationImpedanceStartPolicy.Evaluate(Channels);
        if (impedanceAssessment.EligibleChannels.Count == 0)
        {
            toastService.ShowError("同步开始失败", "没有阻抗状态允许启动的通道。");
            return;
        }

        var synchronizedChannels = impedanceAssessment.EligibleChannels.ToArray();

        var snapshots = new Dictionary<ChannelConfig, DirectCurrentWaveformParameters>();
        foreach (var channel in synchronizedChannels)
        {
            if (!DirectCurrentWaveformParameters.TryCreate(channel, out var snapshot, out var error))
            {
                toastService.ShowError("参数校验失败", error);
                return;
            }

            snapshots[channel] = snapshot!;
        }

        if (!ConfirmSynchronizedStart(impedanceAssessment))
        {
            return;
        }

        SetStartOperationState(synchronizedChannels, true);
        try
        {
            var group = CreateExecutionGroup(synchronizedChannels);
            var result = await stimulationEngine.StartDirectCurrentGroupAsync(
                group,
                string.Join(" + ", synchronizedChannels.Select(channel => channel.Name)),
                AppliedPrescriptionName,
                cancellationToken);
            var sharedTimestamp = Stopwatch.GetTimestamp();
            foreach (var channel in synchronizedChannels)
            {
                BeginChannelRuntime(channel, snapshots[channel], sharedTimestamp);
            }

            HardwareOperationCompleted?.Invoke(this, result);
        }
        finally
        {
            SetStartOperationState(synchronizedChannels, false);
        }
    }

    private async Task StartChannelAsync(
        ChannelConfig channel,
        CancellationToken cancellationToken)
    {
        if (!Channels.Contains(channel))
        {
            return;
        }


        if (activeChannels.ContainsKey(channel))
        {
            toastService.ShowInformation($"{channel.Name} 正在运行。", "开始刺激");
            return;
        }

        await EnsureFreshImpedanceAsync(cancellationToken);
        var impedanceAssessment = StimulationImpedanceStartPolicy.Evaluate([channel]);
        if (impedanceAssessment.EligibleChannels.Count == 0)
        {
            toastService.ShowError(
                "无法开始刺激",
                StimulationImpedanceStartPolicy.BuildSingleChannelBlockedMessage(channel));
            return;
        }

        if (!DirectCurrentWaveformParameters.TryCreate(channel, out var snapshot, out var error))
        {
            toastService.ShowError("参数校验失败", error);
            return;
        }

        if (!ConfirmSingleChannelStart(channel, snapshot!, impedanceAssessment))
        {
            return;
        }

        var group = CreateExecutionGroup([channel]);
        SetStartOperationState([channel], true);
        try
        {
            var result = await stimulationEngine.StartDirectCurrentGroupAsync(
                group,
                channel.Name,
                AppliedPrescriptionName,
                cancellationToken);
            BeginChannelRuntime(channel, snapshot!, Stopwatch.GetTimestamp());
            HardwareOperationCompleted?.Invoke(this, result);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception startException)
        {
            await TryStopAfterUnconfirmedStartAsync(group, channel.Name, startException);
            throw;
        }
        finally
        {
            SetStartOperationState([channel], false);
        }
    }

    private async Task EmergencyStopAsync(CancellationToken cancellationToken)
    {
        CancelPendingStartOperations();
        var running = activeChannels.Keys.ToArray();
        var stoppedAt = Stopwatch.GetTimestamp();
        var group = CreateExecutionGroup(running);
        SetEmergencyStopCooldownState(true);
        try
        {
            var result = await stimulationEngine.EmergencyStopDirectCurrentGroupAsync(
                group,
                "用户点击急停",
                cancellationToken);
            foreach (var channel in running)
            {
                if (!activeChannels.TryGetValue(channel, out var runtime))
                {
                    continue;
                }

                FinalizeStoppedChannel(
                    channel,
                    Stopwatch.GetElapsedTime(runtime.StartTimestamp, stoppedAt).TotalSeconds,
                    completed: false);
            }

            StopTimerWhenIdle();
            SetEmergencyStopCooldownState(true);
            toastService.ShowSuccess("紧急停止", "背板急停命令已发送，设备稳定期间请等待2秒。");
            HardwareOperationCompleted?.Invoke(this, result);
            await Task.Delay(EmergencyStopCooldown);
        }
        finally
        {
            SetEmergencyStopCooldownState(false);
        }
    }

    private async Task StopChannelAsync(
        ChannelConfig channel,
        CancellationToken cancellationToken)
    {
        CancelPendingStartOperations();
        if (!activeChannels.TryGetValue(channel, out var runtime))
        {
            return;
        }

        var group = CreateExecutionGroup([channel]);
        var result = await stimulationEngine.StopGroupAsync(
            group,
            channel.Name,
            "tDCS",
            cancellationToken);
        var elapsed = Stopwatch.GetElapsedTime(runtime.StartTimestamp, Stopwatch.GetTimestamp()).TotalSeconds;
        FinalizeStoppedChannel(channel, elapsed, completed: false);
        StopTimerWhenIdle();
        RefreshCommandStates();
        logger.Info($"tDCS 指定通道停止成功：{channel.Name}");
        HardwareOperationCompleted?.Invoke(this, result);
    }

    private void BeginChannelRuntime(
        ChannelConfig channel,
        DirectCurrentWaveformParameters snapshot,
        long startTimestamp)
    {
        completionPendingChannels.Remove(channel);
        completionStopFailedChannels.Remove(channel);
        impedanceStopPendingChannels.Remove(channel);
        channel.DirectCurrentWaveform.Start(snapshot);
        channel.RemainingTime = FormatRemaining(snapshot.TotalDurationSeconds);
        channel.IsParameterEditingEnabled = false;
        channel.IsStimulating = true;
        activeChannels[channel] = new ChannelRuntime(startTimestamp, snapshot);
        if (!waveformTimer.IsEnabled)
        {
            waveformTimer.Start();
        }

        RefreshCommandStates();
    }

    private void OnWaveformTimerTick(object? sender, EventArgs e)
    {
        if (activeChannels.Count == 0)
        {
            waveformTimer.Stop();
            return;
        }

        var now = Stopwatch.GetTimestamp();
        var completed = new List<ChannelConfig>();
        foreach (var pair in activeChannels.ToArray())
        {
            var channel = pair.Key;
            var runtime = pair.Value;
            var elapsed = Stopwatch.GetElapsedTime(runtime.StartTimestamp, now).TotalSeconds;
            channel.DirectCurrentWaveform.UpdateElapsed(elapsed);
            channel.RemainingTime = FormatRemaining(runtime.Parameters.TotalDurationSeconds - elapsed);
            if (elapsed < runtime.Parameters.TotalDurationSeconds)
            {
                continue;
            }

            channel.RemainingTime = "00:00:00";
            if (!completionPendingChannels.Contains(channel)
                && !completionStopFailedChannels.Contains(channel))
            {
                completionPendingChannels.Add(channel);
                completed.Add(channel);
            }
        }

        if (completed.Count == 0)
        {
            return;
        }

        _ = CompleteChannelsAsync(completed);
    }

    private void RequestBack()
    {
        if (activeChannels.Count > 0 || startOperationInProgress)
        {
            toastService.ShowInformation(
                startOperationInProgress
                    ? "刺激正在启动，请等待本次操作完成或使用紧急停止。"
                    : "刺激正在运行，请等待刺激完成或使用紧急停止后再离开当前界面。",
                "无法离开");
            return;
        }

        BackRequested?.Invoke(this, EventArgs.Empty);
    }

    private void StopTimerWhenIdle()
    {
        if (activeChannels.Count == 0)
        {
            waveformTimer.Stop();
        }
    }

    private void RefreshCommandStates()
    {
        synchronizedStartCommand.RaiseCanExecuteChanged();
        startChannelCommand.RaiseCanExecuteChanged();
        stopChannelCommand.RaiseCanExecuteChanged();
        emergencyStopCommand.RaiseCanExecuteChanged();
        usePrescriptionCommand.RaiseCanExecuteChanged();
        useChannelPrescriptionCommand.RaiseCanExecuteChanged();
    }

    private void OnHardwareConnectionChanged(
        object? sender,
        HardwareConnectionChangedEventArgs eventArgs)
    {
        RefreshCommandStatesOnUiThread();
    }

    private void OnDebugSimulationConnectionChanged(object? sender, EventArgs eventArgs)
    {
        ApplyDebugImpedanceSnapshotIfAvailable();
        RefreshCommandStatesOnUiThread();
    }

    private bool CanStartStimulation =>
        CanControlHardware
        && !startOperationInProgress
        && !emergencyStopCooldownInProgress;

    private bool CanControlHardware =>
        hardwareConnectionState.IsConnected || debugHardwareSimulation.IsConnected;

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
        if (snapshot is null)
        {
            foreach (var channel in Channels)
            {
                channel.UpdateImpedance(null);
                previousImpedanceStatuses[channel] = channel.ImpedanceStatus;
            }

            RefreshCommandStates();
            return;
        }

        var snapshotChannels = snapshot.Channels.ToDictionary(
            channel => channel.LogicalChannelNumber);
        var values = snapshotChannels.ToDictionary(
            channel => channel.Key,
            channel => channel.Value.ImpedanceOhms);
        var warningTransitions = new List<ChannelConfig>();
        var unsafeRunningChannels = new List<(ChannelConfig Channel, byte? BoardAddress)>();
        for (var index = 0; index < Channels.Count; index++)
        {
            var channel = Channels[index];
            var previousStatus = previousImpedanceStatuses.GetValueOrDefault(
                channel,
                StimulationImpedanceStatus.Unavailable);
            channel.UpdateImpedance(values.GetValueOrDefault(index + 1));
            var currentStatus = channel.ImpedanceStatus;
            previousImpedanceStatuses[channel] = currentStatus;
            if (!activeChannels.ContainsKey(channel))
            {
                continue;
            }

            if (currentStatus == StimulationImpedanceStatus.Warning
                && previousStatus != StimulationImpedanceStatus.Warning)
            {
                warningTransitions.Add(channel);
            }

            if ((currentStatus is StimulationImpedanceStatus.Critical
                    or StimulationImpedanceStatus.Unavailable)
                && !impedanceStopPendingChannels.Contains(channel))
            {
                impedanceStopPendingChannels.Add(channel);
                unsafeRunningChannels.Add((
                    channel,
                    snapshotChannels.GetValueOrDefault(index + 1)?.BoardAddress));
            }
        }

        if (warningTransitions.Count > 0)
        {
            toastService.Show(
                ToastKind.Warning,
                "阻抗偏高",
                string.Join("、", warningTransitions.Select(channel =>
                    $"{channel.Name.Replace(" ", string.Empty, StringComparison.Ordinal)} "
                    + $"{channel.ImpedanceOhms!.Value / 1000m:0.00}kΩ")));
        }

        if (unsafeRunningChannels.Count > 0)
        {
            var boardGroups = unsafeRunningChannels
                .GroupBy(item => item.BoardAddress)
                .Select(group => (IReadOnlyList<ChannelConfig>)group
                    .Select(item => item.Channel)
                    .ToArray())
                .ToArray();
            _ = StopUnsafeChannelGroupsAsync(boardGroups);
        }

        RefreshCommandStates();
    }

    private async Task StopUnsafeChannelGroupsAsync(
        IReadOnlyList<IReadOnlyList<ChannelConfig>> boardGroups)
    {
        foreach (var channels in boardGroups)
        {
            await StopChannelsForUnsafeImpedanceAsync(channels);
        }
    }

    private async Task StopChannelsForUnsafeImpedanceAsync(
        IReadOnlyList<ChannelConfig> channels)
    {
        try
        {
            var group = CreateExecutionGroup(channels);
            var result = await stimulationEngine.StopGroupAsync(
                group,
                string.Join(" + ", channels.Select(channel => channel.Name)),
                "tDCS");
            var stoppedAt = Stopwatch.GetTimestamp();
            foreach (var channel in channels)
            {
                var elapsed = activeChannels.TryGetValue(channel, out var runtime)
                    ? Stopwatch.GetElapsedTime(runtime.StartTimestamp, stoppedAt).TotalSeconds
                    : 0;
                FinalizeStoppedChannel(channel, elapsed, completed: false);
            }

            StopTimerWhenIdle();
            RefreshCommandStates();
            toastService.Show(
                ToastKind.Warning,
                "阻抗异常，刺激已停止",
                string.Join("、", channels.Select(channel =>
                    $"{channel.Name.Replace(" ", string.Empty, StringComparison.Ordinal)}："
                    + (channel.ImpedanceStatus == StimulationImpedanceStatus.Critical
                        ? "阻抗超过20kΩ"
                        : "阻抗不可监控"))));
            HardwareOperationCompleted?.Invoke(this, result);
        }
        catch (Exception exception)
        {
            foreach (var channel in channels)
            {
                impedanceStopPendingChannels.Remove(channel);
            }

            logger.Error("阻抗异常触发的指定通道停止失败", exception);
            toastService.ShowError(
                "阻抗安全停止失败",
                "指定通道停止未确认，请立即人工点击紧急停止。");
        }
    }

    private void RefreshCommandStatesOnUiThread()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            _ = dispatcher.BeginInvoke(RefreshCommandStates);
            return;
        }

        RefreshCommandStates();
    }

    private static string FormatRemaining(double seconds)
    {
        var wholeSeconds = Math.Max(0, (int)Math.Ceiling(seconds));
        var hours = wholeSeconds / 3600;
        var minutes = wholeSeconds % 3600 / 60;
        var remainingSeconds = wholeSeconds % 60;
        return $"{hours:00}:{minutes:00}:{remainingSeconds:00}";
    }

    private async Task CompleteChannelsAsync(IReadOnlyList<ChannelConfig> channels)
    {
        try
        {
            var validChannels = channels.Where(Channels.Contains).ToArray();
            if (validChannels.Length == 0)
            {
                return;
            }

            var group = CreateExecutionGroup(validChannels);
            var result = await stimulationEngine.CompleteGroupAsync(
                group,
                string.Join(" + ", validChannels.Select(channel => channel.Name)),
                "tDCS");
            foreach (var channel in validChannels)
            {
                FinalizeStoppedChannel(
                    channel,
                    activeChannels.TryGetValue(channel, out var runtime)
                        ? runtime.Parameters.TotalDurationSeconds
                        : 0,
                    completed: true);
            }

            StopTimerWhenIdle();
            RefreshCommandStates();
            HardwareOperationCompleted?.Invoke(this, result);
        }
        catch (Exception ex)
        {
            foreach (var channel in channels)
            {
                completionPendingChannels.Remove(channel);
                completionStopFailedChannels.Add(channel);
            }

            logger.Error(
                $"tDCS 通道自然结束停止失败：{string.Join("、", channels.Select(channel => channel.Name))}",
                ex);
            toastService.ShowError(
                "自动停止失败",
                "刺激时间已结束，但指定通道停止未确认。请点击通道停止或使用紧急停止。");
        }
    }

    private void FinalizeStoppedChannel(
        ChannelConfig channel,
        double elapsedSeconds,
        bool completed)
    {
        activeChannels.Remove(channel);
        completionPendingChannels.Remove(channel);
        completionStopFailedChannels.Remove(channel);
        impedanceStopPendingChannels.Remove(channel);
        if (completed)
        {
            channel.DirectCurrentWaveform.Complete();
        }
        else
        {
            channel.DirectCurrentWaveform.EmergencyStop(elapsedSeconds);
        }

        channel.RemainingTime = "00:00:00";
        channel.IsParameterEditingEnabled = true;
        channel.IsStimulating = false;
    }

    private void SetStartOperationState(
        IEnumerable<ChannelConfig> channels,
        bool isStarting)
    {
        startOperationInProgress = isStarting;
        foreach (var channel in channels)
        {
            channel.IsStarting = isStarting;
        }

        RefreshCommandStates();
    }

    private void SetEmergencyStopCooldownState(bool isActive)
    {
        emergencyStopCooldownInProgress = isActive;
        foreach (var channel in Channels)
        {
            channel.IsParameterEditingEnabled = !isActive
                && !activeChannels.ContainsKey(channel)
                && !channel.IsStarting;
        }

        RefreshCommandStates();
    }

    private bool ConfirmSynchronizedStart(
        StimulationImpedanceStartAssessment<ChannelConfig> impedanceAssessment)
    {
        var message = impedanceAssessment.RequiresConfirmation
            ? StimulationImpedanceStartPolicy.BuildConfirmationMessage(impedanceAssessment)
            : $"即将同步开始{impedanceAssessment.EligibleChannels.Count}个通道的经颅直流电刺激。"
                + "\n\n请确认各通道刺激参数、阻抗和连接状态无误。";
        return userDialogService.ConfirmWarning(
            "同步开始确认",
            message,
            "确认开始",
            "取消");
    }

    private bool ConfirmSingleChannelStart(
        ChannelConfig channel,
        DirectCurrentWaveformParameters parameters,
        StimulationImpedanceStartAssessment<ChannelConfig> impedanceAssessment)
    {
        double? singleDurationSeconds = parameters.IsContinuous
            ? null
            : parameters.RampUpSeconds + parameters.PlateauSeconds + parameters.RampDownSeconds;
        return userDialogService.ConfirmDirectCurrentStart(
            new DirectCurrentStartConfirmationRequest(
                channel.Name,
                parameters.CurrentMilliamp,
                parameters.IsContinuous,
                parameters.ReversePolarity,
                parameters.RampUpSeconds,
                parameters.RampDownSeconds,
                parameters.TotalDurationSeconds,
                singleDurationSeconds,
                parameters.IsContinuous ? null : parameters.IntervalSeconds,
                channel.ImpedanceOhms!.Value,
                impedanceAssessment.WarningChannels.Count > 0));
    }

    private static bool IsChannelEligibleToStart(ChannelConfig channel) =>
        channel.ImpedanceStatus is StimulationImpedanceStatus.Normal
            or StimulationImpedanceStatus.Warning;

    private void CancelPendingStartOperations()
    {
        synchronizedStartCommand.Cancel();
        startChannelCommand.Cancel();
    }

    private async Task TryStopAfterUnconfirmedStartAsync(
        TiGroup group,
        string channelName,
        Exception startException)
    {
        try
        {
            _ = await stimulationEngine.StopGroupAsync(
                group,
                channelName,
                "tDCS",
                CancellationToken.None);
        }
        catch (Exception stopException)
        {
            logger.Error(
                $"{channelName}启动未确认，随后指定通道停止也失败",
                new AggregateException(startException, stopException));
            toastService.ShowError(
                "启动状态不确定",
                $"{channelName}启动未确认，且安全停止也未确认。请立即点击紧急停止。");
        }
    }

    private static TiGroup CreateExecutionGroup(IEnumerable<ChannelConfig> channels)
    {
        var group = new TiGroup { Title = "经颅直流电刺激" };
        foreach (var channel in channels)
        {
            group.Channels.Add(channel);
        }

        return group;
    }

    private sealed record ChannelRuntime(long StartTimestamp, DirectCurrentWaveformParameters Parameters);
}
