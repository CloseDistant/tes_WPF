using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace RuinaoSoftwareWpf;

/// <summary>经颅脉冲电流刺激的 16 通道参数、波形预览和硬件安全控制。</summary>
public sealed class PulseCurrentControlViewModel : ObservableObject, IDisposable
{
    private readonly IStimulationEngine stimulationEngine;
    private readonly IHardwareConnectionState hardwareConnectionState;
    private readonly IHardwareService? hardwareService;
    private readonly IDebugHardwareSimulationService debugHardwareSimulation;
    private readonly IDebugStimulationImpedanceProvider? debugImpedanceProvider;
    private readonly IToastService toastService;
    private readonly ILoggingService logger;
    private readonly IUserDialogService userDialogService;
    private readonly DispatcherTimer waveformTimer;
    private readonly Dictionary<PulseCurrentChannelConfig, ChannelRuntime> activeChannels = [];
    private readonly Dictionary<PulseCurrentChannelConfig, PulseCurrentParameters> unknownChannels = [];
    private readonly HashSet<PulseCurrentChannelConfig> completionPendingChannels = [];
    private readonly HashSet<PulseCurrentChannelConfig> completionStopFailedChannels = [];
    private readonly HashSet<PulseCurrentChannelConfig> impedanceStopPendingChannels = [];
    private readonly AsyncRelayCommand synchronizedStartCommand;
    private readonly AsyncRelayCommand startChannelCommand;
    private readonly AsyncRelayCommand stopChannelCommand;
    private readonly AsyncRelayCommand emergencyStopCommand;
    private readonly RelayCommand usePrescriptionCommand;
    private readonly RelayCommand useChannelPrescriptionCommand;
    private PulseCurrentChannelPair? selectedChannelPair;
    private PulseCurrentChannelConfig? selectedChannel;
    private string appliedPrescriptionName = "手动设置";
    private bool startOperationInProgress;
    private bool disposed;

    public PulseCurrentControlViewModel(
        IStimulationEngine stimulationEngine,
        IHardwareConnectionState hardwareConnectionState,
        IDebugHardwareSimulationService debugHardwareSimulation,
        LocalizationViewModel localization,
        IToastService toastService,
        ILoggingService logger,
        IUserDialogService userDialogService,
        IDebugStimulationImpedanceProvider? debugImpedanceProvider = null)
    {
        this.stimulationEngine = stimulationEngine;
        this.hardwareConnectionState = hardwareConnectionState;
        hardwareService = hardwareConnectionState as IHardwareService;
        this.debugHardwareSimulation = debugHardwareSimulation;
        this.debugImpedanceProvider = debugImpedanceProvider;
        this.toastService = toastService;
        this.logger = logger;
        this.userDialogService = userDialogService;
        Localization = localization;

        Channels = new ObservableCollection<PulseCurrentChannelConfig>(
            Enumerable.Range(1, 16).Select(channelNumber => new PulseCurrentChannelConfig
            {
                Name = $"CH {channelNumber}",
                Polarity = PulseCurrentPolarities.NotReversed
            }));
        ChannelPairs = new ObservableCollection<PulseCurrentChannelPair>(
            Enumerable.Range(0, 8).Select(pairIndex => new PulseCurrentChannelPair(
                pairIndex + 1,
                Channels[pairIndex * 2],
                Channels[pairIndex * 2 + 1])));

        waveformTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        waveformTimer.Tick += OnWaveformTimerTick;

        BackCommand = new RelayCommand(_ => RequestBack());
        SelectChannelCommand = new RelayCommand(SelectChannel);
        synchronizedStartCommand = new AsyncRelayCommand(
            (_, token) => StartSynchronizedAsync(token),
            _ => CanStart && activeChannels.Count == 0 && unknownChannels.Count == 0,
            HandleStartFailure);
        startChannelCommand = new AsyncRelayCommand(
            (parameter, token) => parameter is PulseCurrentChannelConfig channel
                ? StartChannelAsync(channel, token)
                : Task.CompletedTask,
            parameter => CanStart
                && parameter is PulseCurrentChannelConfig channel
                && Channels.Contains(channel)
                && !activeChannels.ContainsKey(channel)
                && !unknownChannels.ContainsKey(channel)
                && IsImpedanceEligible(channel),
            HandleStartFailure);
        stopChannelCommand = new AsyncRelayCommand(
            (parameter, token) => parameter is PulseCurrentChannelConfig channel
                ? StopChannelAsync(channel, token)
                : Task.CompletedTask,
            parameter => CanControlHardware
                && parameter is PulseCurrentChannelConfig channel
                && activeChannels.ContainsKey(channel),
            HandleStopFailure);
        emergencyStopCommand = new AsyncRelayCommand(
            (_, token) => EmergencyStopAsync(token),
            _ => CanControlHardware,
            HandleEmergencyStopFailure);
        usePrescriptionCommand = new RelayCommand(
            _ => RequestPrescription(StimulationPrescriptionApplyScope.AllChannels),
            _ => activeChannels.Count == 0 && unknownChannels.Count == 0 && !startOperationInProgress);
        useChannelPrescriptionCommand = new RelayCommand(
            parameter => RequestPrescription(StimulationPrescriptionApplyScope.SingleChannel, parameter),
            parameter => parameter is PulseCurrentChannelConfig channel
                && Channels.Contains(channel)
                && !activeChannels.ContainsKey(channel)
                && !unknownChannels.ContainsKey(channel)
                && !startOperationInProgress);

        SynchronizedStartCommand = synchronizedStartCommand;
        StartChannelCommand = startChannelCommand;
        StopChannelCommand = stopChannelCommand;
        EmergencyStopCommand = emergencyStopCommand;
        UsePrescriptionCommand = usePrescriptionCommand;
        UseChannelPrescriptionCommand = useChannelPrescriptionCommand;
        ParameterValidationFailedCommand = new RelayCommand(parameter =>
        {
            if (parameter is string message && !string.IsNullOrWhiteSpace(message))
            {
                toastService.Show(ToastKind.Warning, "参数已调整", message);
            }
        });
        RefreshPlannedTotalCountCommand = new RelayCommand(parameter =>
        {
            if (parameter is PulseCurrentChannelConfig channel && Channels.Contains(channel))
            {
                RefreshPlannedTotalCount(channel);
            }
        });

        hardwareConnectionState.ConnectionChanged += OnConnectionChanged;
        debugHardwareSimulation.ConnectionChanged += OnDebugConnectionChanged;
        if (hardwareService is not null)
        {
            hardwareService.StimulationImpedanceChanged += OnImpedanceChanged;
        }

        SelectedChannelPair = ChannelPairs[0];
        SelectedChannel = Channels[0];
        ApplyDebugImpedanceIfAvailable();
    }

    public LocalizationViewModel Localization { get; }
    public ObservableCollection<PulseCurrentChannelConfig> Channels { get; }
    public ObservableCollection<PulseCurrentChannelPair> ChannelPairs { get; }
    public IReadOnlyList<string> Polarities => PulseCurrentPolarities.All;
    public IReadOnlyList<PulseCurrentChannelConfig> SelectedChannels =>
        SelectedChannelPair?.Channels ?? Array.Empty<PulseCurrentChannelConfig>();
    public ICommand BackCommand { get; }
    public ICommand SelectChannelCommand { get; }
    public ICommand SynchronizedStartCommand { get; }
    public ICommand StartChannelCommand { get; }
    public ICommand StopChannelCommand { get; }
    public ICommand EmergencyStopCommand { get; }
    public ICommand UsePrescriptionCommand { get; }
    public ICommand UseChannelPrescriptionCommand { get; }
    public ICommand ParameterValidationFailedCommand { get; }
    public ICommand RefreshPlannedTotalCountCommand { get; }

    public PulseCurrentChannelPair? SelectedChannelPair
    {
        get => selectedChannelPair;
        private set
        {
            if (SetProperty(ref selectedChannelPair, value))
            {
                OnPropertyChanged(nameof(SelectedChannels));
            }
        }
    }

    public PulseCurrentChannelConfig? SelectedChannel
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

    public event EventHandler? BackRequested;
    public event EventHandler<HardwareOperationResult>? HardwareOperationCompleted;
    public event EventHandler<StimulationPrescriptionRequestEventArgs>? PrescriptionRequested;

    public bool TryApplyPrescription(PrescriptionDefinition prescription, out string error) =>
        TryApplyPrescription(prescription, Channels, out error);

    public bool TryApplyPrescription(
        PrescriptionDefinition prescription,
        PulseCurrentChannelConfig channel,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(channel);
        return TryApplyPrescription(prescription, [channel], out error);
    }

    private bool TryApplyPrescription(
        PrescriptionDefinition prescription,
        IEnumerable<PulseCurrentChannelConfig> targetChannels,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(prescription);
        var targets = targetChannels.ToArray();
        if (!prescription.IsPulseCurrent)
        {
            error = "处方不是 tPCS 类型。";
            return false;
        }

        if (!prescription.HasPulseCurrentParameters)
        {
            error = "tPCS 处方参数不完整。";
            return false;
        }

        if (targets.Any(channel => activeChannels.ContainsKey(channel) || unknownChannels.ContainsKey(channel)))
        {
            error = "目标通道正在刺激，不能应用处方。";
            return false;
        }

        var currentText = PulseCurrentParameterRules.FormatCurrent(prescription.CurrentMilliamp);
        var treatmentDurationText = PulseCurrentParameterRules.FormatTreatmentDuration(
            prescription.PulseTreatmentDurationSecondsResolved);
        var pulseWidthText = prescription.PulseWidthMilliseconds!.Value.ToString(CultureInfo.InvariantCulture);
        var riseWidthText = prescription.PulseRiseWidthMilliseconds!.Value.ToString(CultureInfo.InvariantCulture);
        var intervalWidthText = prescription.PulseIntervalWidthMilliseconds!.Value.ToString(CultureInfo.InvariantCulture);
        foreach (var channel in targets)
        {
            channel.CurrentMilliamp = currentText;
            channel.TreatmentDurationSeconds = treatmentDurationText;
            channel.PulseWidthMilliseconds = pulseWidthText;
            channel.RiseWidthMilliseconds = riseWidthText;
            channel.IntervalWidthMilliseconds = intervalWidthText;
            RefreshPlannedTotalCount(channel);
            channel.RemainingTime = "00:00:00";
            channel.Waveform.Clear();
            channel.RefreshBindings();
        }

        appliedPrescriptionName = prescription.Name;
        OnPropertyChanged(nameof(SelectedChannels));
        error = string.Empty;
        return true;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        waveformTimer.Stop();
        waveformTimer.Tick -= OnWaveformTimerTick;
        hardwareConnectionState.ConnectionChanged -= OnConnectionChanged;
        debugHardwareSimulation.ConnectionChanged -= OnDebugConnectionChanged;
        if (hardwareService is not null)
        {
            hardwareService.StimulationImpedanceChanged -= OnImpedanceChanged;
        }
    }

    private async Task StartSynchronizedAsync(CancellationToken cancellationToken)
    {
        if (!userDialogService.ConfirmWarning(
                "同步开始确认",
                "系统将检查全部16个通道，并启动所有参数合法且阻抗允许的通道。确认后将继续校验参数和阻抗状态。",
                "确认同步开始",
                "取消"))
        {
            return;
        }

        await EnsureFreshImpedanceAsync(cancellationToken);
        var assessment = StimulationImpedanceStartPolicy.Evaluate(Channels);
        if (assessment.EligibleChannels.Count == 0)
        {
            toastService.ShowError("同步开始失败", "没有阻抗状态允许启动的通道。");
            return;
        }

        if (assessment.RequiresConfirmation
            && !userDialogService.ConfirmWarning(
                "阻抗状态确认",
                StimulationImpedanceStartPolicy.BuildConfirmationMessage(assessment),
                "确认并继续",
                "取消"))
        {
            return;
        }

        var targets = assessment.EligibleChannels.ToArray();
        if (!TryCreateSnapshots(targets, out var snapshots))
        {
            return;
        }

        SetStarting(targets, true);
        try
        {
            var group = CreateGroup(targets, snapshots);
            var result = await stimulationEngine.StartPulseCurrentGroupAsync(
                group,
                string.Join(" + ", targets.Select(channel => channel.Name)),
                appliedPrescriptionName,
                cancellationToken);
            var timestamp = Stopwatch.GetTimestamp();
            foreach (var channel in targets)
            {
                BeginChannelRuntime(channel, snapshots[channel], timestamp);
            }

            HardwareOperationCompleted?.Invoke(this, result);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            foreach (var channel in targets)
            {
                MoveToUnknown(channel, snapshots[channel], 0);
            }

            if (userDialogService.ConfirmWarning(
                    "同步启动状态未知",
                    $"以下通道启动返回超时或失败：{string.Join("、", targets.Select(channel => channel.Name))}。是否立即执行背板级紧急停止？",
                    "紧急停止",
                    "暂不处理"))
            {
                await EmergencyStopAsync(CancellationToken.None);
            }

            throw;
        }
        finally
        {
            SetStarting(targets, false);
        }
    }

    private async Task StartChannelAsync(
        PulseCurrentChannelConfig channel,
        CancellationToken cancellationToken)
    {
        if (!Channels.Contains(channel) || activeChannels.ContainsKey(channel))
        {
            return;
        }

        await EnsureFreshImpedanceAsync(cancellationToken);
        var assessment = StimulationImpedanceStartPolicy.Evaluate([channel]);
        if (assessment.EligibleChannels.Count == 0)
        {
            toastService.ShowError(
                "无法开始刺激",
                StimulationImpedanceStartPolicy.BuildSingleChannelBlockedMessage(channel));
            return;
        }

        if (!PulseCurrentParameters.TryCreate(channel, out var snapshot, out var error))
        {
            toastService.ShowError("参数校验失败", $"{channel.Name}：{error}");
            return;
        }

        if (!userDialogService.ConfirmWarning(
                "开始刺激确认",
                $"{channel.Name}\n\n幅值：{snapshot!.CurrentMilliamp:0.00} mA\n"
                    + $"上升宽度：{snapshot.RiseWidthMilliseconds} ms\n"
                    + $"脉冲宽度：{snapshot.PulseWidthMilliseconds} ms\n"
                    + $"间隔宽度：{snapshot.IntervalWidthMilliseconds} ms\n"
                    + $"治疗时间：{snapshot.TreatmentDurationSeconds:0.0} s\n"
                    + $"阻抗：{channel.ImpedanceOhms:0.##} Ω",
                "确认并开始",
                "返回修改"))
        {
            return;
        }

        SetStarting([channel], true);
        var snapshots = new Dictionary<PulseCurrentChannelConfig, PulseCurrentParameters>
        {
            [channel] = snapshot
        };
        try
        {
            var result = await stimulationEngine.StartPulseCurrentGroupAsync(
                CreateGroup([channel], snapshots),
                channel.Name,
                appliedPrescriptionName,
                cancellationToken);
            BeginChannelRuntime(channel, snapshot, Stopwatch.GetTimestamp());
            HardwareOperationCompleted?.Invoke(this, result);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            MoveToUnknown(channel, snapshot, 0);
            if (userDialogService.ConfirmWarning(
                    "启动状态未知",
                    $"{channel.Name}启动命令返回超时或失败，无法确认实际状态。是否立即执行背板级紧急停止？",
                    "紧急停止",
                    "暂不处理"))
            {
                await EmergencyStopAsync(CancellationToken.None);
            }

            throw;
        }
        finally
        {
            SetStarting([channel], false);
        }
    }

    private void BeginChannelRuntime(
        PulseCurrentChannelConfig channel,
        PulseCurrentParameters snapshot,
        long startTimestamp)
    {
        channel.ShowPlannedTotalCount(snapshot.PlannedTotalCount);
        channel.Waveform.Start(snapshot);
        channel.RemainingTime = FormatRemaining(snapshot.TotalRuntimeSeconds);
        channel.IsParameterEditingEnabled = false;
        channel.IsStimulating = true;
        activeChannels[channel] = new ChannelRuntime(startTimestamp, snapshot);
        completionPendingChannels.Remove(channel);
        completionStopFailedChannels.Remove(channel);
        impedanceStopPendingChannels.Remove(channel);
        if (!waveformTimer.IsEnabled)
        {
            waveformTimer.Start();
        }

        RefreshCommandStates();
    }

    private async Task StopChannelAsync(
        PulseCurrentChannelConfig channel,
        CancellationToken cancellationToken)
    {
        if (!activeChannels.TryGetValue(channel, out var runtime)
            || !userDialogService.ConfirmWarning(
                "停止刺激确认",
                $"即将停止 {channel.Name}。",
                "确认停止",
                "取消"))
        {
            return;
        }

        HardwareOperationResult result;
        try
        {
            result = await stimulationEngine.StopGroupAsync(
                CreateGroup([channel], new Dictionary<PulseCurrentChannelConfig, PulseCurrentParameters>
                {
                    [channel] = runtime.Parameters
                }),
                channel.Name,
                StimulationModeCodes.PulseCurrent,
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            MoveToUnknown(
                channel,
                runtime.Parameters,
                Stopwatch.GetElapsedTime(runtime.StartTimestamp).TotalSeconds);
            if (userDialogService.ConfirmWarning(
                    "停止失败",
                    $"{channel.Name}停止命令未确认。是否立即执行背板级紧急停止？",
                    "紧急停止",
                    "暂不处理"))
            {
                await EmergencyStopAsync(CancellationToken.None);
            }

            throw;
        }

        FinalizeChannel(channel, Stopwatch.GetElapsedTime(runtime.StartTimestamp).TotalSeconds, false);
        HardwareOperationCompleted?.Invoke(this, result);
        StopTimerWhenIdle();
        RefreshCommandStates();
    }

    private async Task EmergencyStopAsync(CancellationToken cancellationToken)
    {
        synchronizedStartCommand.Cancel();
        startChannelCommand.Cancel();
        var running = activeChannels.ToArray();
        var unknown = unknownChannels.ToArray();
        var allChannels = running.Select(pair => pair.Key)
            .Concat(unknown.Select(pair => pair.Key))
            .Distinct()
            .ToArray();
        var allSnapshots = running.ToDictionary(pair => pair.Key, pair => pair.Value.Parameters);
        foreach (var pair in unknown)
        {
            allSnapshots[pair.Key] = pair.Value;
        }

        var stoppedAt = Stopwatch.GetTimestamp();
        HardwareOperationResult result;
        try
        {
            result = await stimulationEngine.EmergencyStopPulseCurrentGroupAsync(
                CreateGroup(allChannels, allSnapshots),
                "用户点击急停",
                cancellationToken);
        }
        catch
        {
            foreach (var pair in running)
            {
                MoveToUnknown(
                    pair.Key,
                    pair.Value.Parameters,
                    Stopwatch.GetElapsedTime(pair.Value.StartTimestamp, stoppedAt).TotalSeconds);
            }

            throw;
        }

        foreach (var pair in running)
        {
            FinalizeChannel(
                pair.Key,
                Stopwatch.GetElapsedTime(pair.Value.StartTimestamp, stoppedAt).TotalSeconds,
                false);
        }

        foreach (var pair in unknown)
        {
            pair.Key.IsParameterEditingEnabled = true;
        }

        unknownChannels.Clear();

        waveformTimer.Stop();
        RefreshCommandStates();
        HardwareOperationCompleted?.Invoke(this, result);
    }

    private async void OnWaveformTimerTick(object? sender, EventArgs e)
    {
        var now = Stopwatch.GetTimestamp();
        var completed = new List<PulseCurrentChannelConfig>();
        foreach (var pair in activeChannels.ToArray())
        {
            var elapsed = Stopwatch.GetElapsedTime(pair.Value.StartTimestamp, now).TotalSeconds;
            pair.Key.Waveform.UpdateElapsed(elapsed);
            pair.Key.RemainingTime = FormatRemaining(pair.Value.Parameters.TotalRuntimeSeconds - elapsed);
            if (elapsed >= pair.Value.Parameters.TotalRuntimeSeconds
                && !completionStopFailedChannels.Contains(pair.Key)
                && completionPendingChannels.Add(pair.Key))
            {
                completed.Add(pair.Key);
            }
        }

        if (completed.Count == 0)
        {
            return;
        }

        var snapshots = completed.ToDictionary(channel => channel, channel => activeChannels[channel].Parameters);
        try
        {
            var result = await stimulationEngine.CompleteGroupAsync(
                CreateGroup(completed, snapshots),
                string.Join(" + ", completed.Select(channel => channel.Name)),
                StimulationModeCodes.PulseCurrent);
            foreach (var channel in completed)
            {
                FinalizeChannel(channel, snapshots[channel].TotalRuntimeSeconds, true);
            }

            HardwareOperationCompleted?.Invoke(this, result);
        }
        catch (Exception exception)
        {
            logger.Error("tPCS自然结束停止失败", exception);
            foreach (var channel in completed)
            {
                completionPendingChannels.Remove(channel);
                completionStopFailedChannels.Add(channel);
                MoveToUnknown(channel, snapshots[channel], snapshots[channel].TotalRuntimeSeconds);
            }

            if (userDialogService.ConfirmWarning(
                    "自动停止失败",
                    $"以下通道停止命令未确认：{string.Join("、", completed.Select(channel => channel.Name))}。是否立即执行背板级紧急停止？",
                    "紧急停止",
                    "暂不处理"))
            {
                await EmergencyStopAsync(CancellationToken.None);
            }
        }

        StopTimerWhenIdle();
        RefreshCommandStates();
    }

    private void FinalizeChannel(PulseCurrentChannelConfig channel, double elapsedSeconds, bool completed)
    {
        activeChannels.Remove(channel);
        completionPendingChannels.Remove(channel);
        impedanceStopPendingChannels.Remove(channel);
        if (completed)
        {
            channel.Waveform.Complete();
        }
        else
        {
            channel.Waveform.EmergencyStop(elapsedSeconds);
        }

        channel.RemainingTime = "00:00:00";
        channel.IsParameterEditingEnabled = true;
        channel.IsStimulating = false;
    }

    private void MoveToUnknown(
        PulseCurrentChannelConfig channel,
        PulseCurrentParameters parameters,
        double elapsedSeconds)
    {
        activeChannels.Remove(channel);
        completionPendingChannels.Remove(channel);
        impedanceStopPendingChannels.Remove(channel);
        channel.Waveform.EmergencyStop(elapsedSeconds);
        channel.RemainingTime = "00:00:00";
        channel.IsStimulating = false;
        channel.IsParameterEditingEnabled = false;
        unknownChannels[channel] = parameters;
        StopTimerWhenIdle();
        RefreshCommandStates();
    }

    private async Task EnsureFreshImpedanceAsync(CancellationToken cancellationToken)
    {
        if (!hardwareConnectionState.IsConnected || hardwareService is null)
        {
            return;
        }

        if (hardwareService.CurrentStimulationImpedance is not { } snapshot
            || DateTimeOffset.Now - snapshot.CapturedAt > TimeSpan.FromSeconds(10))
        {
            _ = await hardwareService.CheckImpedanceAsync(cancellationToken);
        }

        ApplyImpedance(hardwareService.CurrentStimulationImpedance);
    }

    private void OnImpedanceChanged(object? sender, StimulationImpedanceChangedEventArgs eventArgs)
    {
        void Apply()
        {
            if (!ApplyDebugImpedanceIfAvailable())
            {
                ApplyImpedance(eventArgs.Snapshot);
            }
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            _ = dispatcher.BeginInvoke(Apply);
        }
        else
        {
            Apply();
        }
    }

    private void ApplyImpedance(StimulationImpedanceSnapshot? snapshot)
    {
        var values = snapshot?.Channels.ToDictionary(item => item.LogicalChannelNumber, item => item.ImpedanceOhms)
            ?? new Dictionary<int, decimal?>();
        var unsafeChannels = new List<PulseCurrentChannelConfig>();
        for (var index = 0; index < Channels.Count; index++)
        {
            var channel = Channels[index];
            channel.UpdateImpedance(values.GetValueOrDefault(index + 1));
            if (activeChannels.ContainsKey(channel)
                && channel.ImpedanceStatus is StimulationImpedanceStatus.Critical or StimulationImpedanceStatus.Unavailable
                && impedanceStopPendingChannels.Add(channel))
            {
                unsafeChannels.Add(channel);
            }
        }

        foreach (var group in unsafeChannels.GroupBy(GetLogicalBoardIndex))
        {
            _ = StopUnsafeChannelsAsync(group.ToArray());
        }

        RefreshCommandStates();
    }

    private async Task StopUnsafeChannelsAsync(IReadOnlyList<PulseCurrentChannelConfig> channels)
    {
        var snapshots = channels
            .Where(activeChannels.ContainsKey)
            .ToDictionary(channel => channel, channel => activeChannels[channel].Parameters);
        try
        {
            var result = await stimulationEngine.StopGroupAsync(
                CreateGroup(channels, snapshots),
                string.Join(" + ", channels.Select(channel => channel.Name)),
                StimulationModeCodes.PulseCurrent);
            var stoppedAt = Stopwatch.GetTimestamp();
            foreach (var channel in channels)
            {
                var elapsed = activeChannels.TryGetValue(channel, out var runtime)
                    ? Stopwatch.GetElapsedTime(runtime.StartTimestamp, stoppedAt).TotalSeconds
                    : 0;
                FinalizeChannel(channel, elapsed, false);
            }

            toastService.Show(ToastKind.Warning, "阻抗异常，刺激已停止", string.Join("、", channels.Select(channel => channel.Name)));
            HardwareOperationCompleted?.Invoke(this, result);
        }
        catch (Exception exception)
        {
            foreach (var channel in channels)
            {
                impedanceStopPendingChannels.Remove(channel);
                if (snapshots.TryGetValue(channel, out var parameters))
                {
                    var elapsed = activeChannels.TryGetValue(channel, out var runtime)
                        ? Stopwatch.GetElapsedTime(runtime.StartTimestamp).TotalSeconds
                        : 0;
                    MoveToUnknown(channel, parameters, elapsed);
                }
            }

            logger.Error("tPCS阻抗安全停止失败", exception);
            if (userDialogService.ConfirmWarning(
                    "阻抗安全停止失败",
                    $"以下通道停止未确认：{string.Join("、", channels.Select(channel => channel.Name))}。是否立即执行背板级紧急停止？",
                    "紧急停止",
                    "暂不处理"))
            {
                await EmergencyStopAsync(CancellationToken.None);
            }
        }

        StopTimerWhenIdle();
        RefreshCommandStates();
    }

    private bool TryCreateSnapshots(
        IEnumerable<PulseCurrentChannelConfig> channels,
        out Dictionary<PulseCurrentChannelConfig, PulseCurrentParameters> snapshots)
    {
        snapshots = [];
        foreach (var channel in channels)
        {
            if (!PulseCurrentParameters.TryCreate(channel, out var snapshot, out var error))
            {
                toastService.ShowError("参数校验失败", $"{channel.Name}：{error}");
                snapshots.Clear();
                return false;
            }

            snapshots[channel] = snapshot!;
        }

        return true;
    }

    private static void RefreshPlannedTotalCount(PulseCurrentChannelConfig channel)
    {
        if (PulseCurrentParameters.TryCreate(channel, out var parameters, out _))
        {
            channel.ShowPlannedTotalCount(parameters!.PlannedTotalCount);
        }
        else
        {
            channel.ClearPlannedTotalCount();
        }
    }

    private void RequestPrescription(StimulationPrescriptionApplyScope scope, object? targetChannel = null) =>
        PrescriptionRequested?.Invoke(this, new StimulationPrescriptionRequestEventArgs(
            PrescriptionDefinition.PulseCurrentStimulationType,
            scope,
            targetChannel));

    private void RequestBack()
    {
        if (activeChannels.Count > 0 || startOperationInProgress)
        {
            toastService.ShowInformation(
                "刺激正在运行或启动中，请停止或紧急停止后再离开当前界面。",
                "无法离开");
            return;
        }

        BackRequested?.Invoke(this, EventArgs.Empty);
    }

    private void SelectChannel(object? parameter)
    {
        if (parameter is not PulseCurrentChannelConfig channel || !Channels.Contains(channel))
        {
            return;
        }

        SelectedChannelPair = ChannelPairs.First(pair => pair.Channels.Contains(channel));
        SelectedChannel = channel;
    }

    private void SetStarting(IEnumerable<PulseCurrentChannelConfig> channels, bool value)
    {
        startOperationInProgress = value;
        foreach (var channel in channels)
        {
            channel.IsStarting = value;
        }

        RefreshCommandStates();
    }

    private void OnConnectionChanged(object? sender, HardwareConnectionChangedEventArgs e)
    {
        void Apply()
        {
            if (!e.IsConnected)
            {
                foreach (var pair in activeChannels.ToArray())
                {
                    MoveToUnknown(
                        pair.Key,
                        pair.Value.Parameters,
                        Stopwatch.GetElapsedTime(pair.Value.StartTimestamp).TotalSeconds);
                }
            }

            RefreshCommandStates();
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            _ = dispatcher.BeginInvoke(Apply);
        }
        else
        {
            Apply();
        }
    }

    private void OnDebugConnectionChanged(object? sender, EventArgs e)
    {
        ApplyDebugImpedanceIfAvailable();
        RefreshCommandsOnUiThread();
    }

    private bool ApplyDebugImpedanceIfAvailable()
    {
        if (!debugHardwareSimulation.IsConnected || debugImpedanceProvider?.GetSnapshot() is not { } snapshot)
        {
            return false;
        }

        ApplyImpedance(snapshot);
        return true;
    }

    private void RefreshCommandsOnUiThread()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            _ = dispatcher.BeginInvoke(RefreshCommandStates);
        }
        else
        {
            RefreshCommandStates();
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

    private void StopTimerWhenIdle()
    {
        if (activeChannels.Count == 0)
        {
            waveformTimer.Stop();
        }
    }

    private void HandleStartFailure(Exception exception)
    {
        logger.Error("tPCS启动失败", exception);
        toastService.ShowError("刺激启动失败", "启动命令未完成，软件未进入运行状态。请检查日志和设备状态。");
    }

    private void HandleStopFailure(Exception exception)
    {
        logger.Error("tPCS停止失败", exception);
        toastService.ShowError("刺激停止失败", "停止未确认，通道仍保持运行状态。请再次停止或使用紧急停止。");
    }

    private void HandleEmergencyStopFailure(Exception exception)
    {
        logger.Error("tPCS背板急停失败", exception);
        toastService.ShowError("紧急停止失败", "背板急停未确认，请立即人工检查设备并再次尝试急停。");
    }

    private bool CanStart => CanControlHardware && !startOperationInProgress;
    private bool CanControlHardware => hardwareConnectionState.IsConnected || debugHardwareSimulation.IsConnected;
    private static bool IsImpedanceEligible(PulseCurrentChannelConfig channel) =>
        channel.ImpedanceStatus is StimulationImpedanceStatus.Normal or StimulationImpedanceStatus.Warning;
    private static int GetLogicalBoardIndex(PulseCurrentChannelConfig channel) =>
        (ParseChannelNumber(channel.Name) - 1) / 8;

    private static TiGroup CreateGroup(
        IEnumerable<PulseCurrentChannelConfig> channels,
        IReadOnlyDictionary<PulseCurrentChannelConfig, PulseCurrentParameters> snapshots)
    {
        var group = new TiGroup { Title = "经颅脉冲电流刺激" };
        foreach (var channel in channels)
        {
            var parameters = snapshots[channel];
            group.Channels.Add(new ChannelConfig
            {
                Name = channel.Name,
                CurrentMA = PulseCurrentParameterRules.FormatCurrent(parameters.CurrentMilliamp),
                RampUpS = (parameters.RiseWidthMilliseconds / 1000d).ToString("0.###", CultureInfo.InvariantCulture),
                RampDownS = "0",
                DurationS = PulseCurrentParameterRules.FormatTreatmentDuration(parameters.TreatmentDurationSeconds),
                IntervalS = (parameters.IntervalWidthMilliseconds / 1000d).ToString("0.###", CultureInfo.InvariantCulture),
                SingleDurationS = (parameters.PulseWidthMilliseconds / 1000d).ToString("0.###", CultureInfo.InvariantCulture),
                Polarity = parameters.Polarity,
                StimulationMode = "间隔",
                PulseWidthMilliseconds = parameters.PulseWidthMilliseconds.ToString(CultureInfo.InvariantCulture),
                PulseRiseWidthMilliseconds = parameters.RiseWidthMilliseconds.ToString(CultureInfo.InvariantCulture),
                PulseIntervalWidthMilliseconds = parameters.IntervalWidthMilliseconds.ToString(CultureInfo.InvariantCulture),
                PlannedPulseCount = parameters.PlannedTotalCount.ToString(CultureInfo.InvariantCulture)
            });
        }

        return group;
    }

    private static int ParseChannelNumber(string name) =>
        int.Parse(new string(name.Where(char.IsDigit).ToArray()), CultureInfo.InvariantCulture);

    private static string FormatRemaining(double seconds)
    {
        var wholeSeconds = Math.Max(0, (int)Math.Ceiling(seconds));
        return $"{wholeSeconds / 3600:00}:{wholeSeconds % 3600 / 60:00}:{wholeSeconds % 60:00}";
    }

    private sealed record ChannelRuntime(long StartTimestamp, PulseCurrentParameters Parameters);
}
