using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace RuinaoSoftwareWpf;

/// <summary>经颅单相脉冲电流刺激的16通道参数、运行快照和安全控制。</summary>
public sealed class MonophasicPulseCurrentControlViewModel : ObservableObject
{
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
    private readonly AsyncRelayCommand synchronizedStartCommand;
    private readonly AsyncRelayCommand startChannelCommand;
    private readonly AsyncRelayCommand stopChannelCommand;
    private readonly AsyncRelayCommand emergencyStopCommand;
    private readonly RelayCommand usePrescriptionCommand;
    private readonly RelayCommand useChannelPrescriptionCommand;
    private StimulationChannelPair? selectedChannelPair;
    private ChannelConfig? selectedChannel;
    private string appliedPrescriptionName = "手动设置";
    private bool startOperationInProgress;

    public MonophasicPulseCurrentControlViewModel(
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
            Enumerable.Range(1, 16).Select(number => CreateChannel($"CH {number}", accent)));
        ChannelPairs = new ObservableCollection<StimulationChannelPair>(
            Enumerable.Range(0, 8).Select(index => new StimulationChannelPair(
                index + 1,
                Channels[index * 2],
                Channels[index * 2 + 1])));

        waveformTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        waveformTimer.Tick += OnWaveformTimerTick;

        BackCommand = new RelayCommand(_ => RequestBack());
        SelectChannelCommand = new RelayCommand(SelectChannel);
        synchronizedStartCommand = new AsyncRelayCommand(
            (_, token) => StartSynchronizedAsync(token),
            _ => CanStart && activeChannels.Count == 0,
            HandleStartFailure);
        startChannelCommand = new AsyncRelayCommand(
            (parameter, token) => parameter is ChannelConfig channel
                ? StartChannelAsync(channel, token)
                : Task.CompletedTask,
            parameter => CanStart
                && parameter is ChannelConfig channel
                && Channels.Contains(channel)
                && !activeChannels.ContainsKey(channel)
                && IsImpedanceEligible(channel),
            HandleStartFailure);
        stopChannelCommand = new AsyncRelayCommand(
            (parameter, token) => parameter is ChannelConfig channel
                ? StopChannelAsync(channel, token)
                : Task.CompletedTask,
            parameter => CanControlHardware
                && parameter is ChannelConfig channel
                && activeChannels.ContainsKey(channel),
            HandleStopFailure);
        emergencyStopCommand = new AsyncRelayCommand(
            (_, token) => EmergencyStopAsync(token),
            _ => CanControlHardware,
            HandleEmergencyStopFailure);
        usePrescriptionCommand = new RelayCommand(
            _ => RequestPrescription(StimulationPrescriptionApplyScope.AllChannels),
            _ => activeChannels.Count == 0 && !startOperationInProgress);
        useChannelPrescriptionCommand = new RelayCommand(
            parameter => RequestPrescription(StimulationPrescriptionApplyScope.SingleChannel, parameter),
            parameter => parameter is ChannelConfig channel
                && Channels.Contains(channel)
                && !activeChannels.ContainsKey(channel)
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
    public ObservableCollection<ChannelConfig> Channels { get; }
    public ObservableCollection<StimulationChannelPair> ChannelPairs { get; }
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

    public StimulationChannelPair? SelectedChannelPair
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

    public event EventHandler? BackRequested;
    public event EventHandler<HardwareOperationResult>? HardwareOperationCompleted;
    public event EventHandler<StimulationPrescriptionRequestEventArgs>? PrescriptionRequested;

    public bool TryApplyPrescription(
        PrescriptionDefinition prescription,
        IEnumerable<ChannelConfig> targets,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(prescription);
        var channels = targets.ToArray();
        if (!string.Equals(prescription.StimulationType, StimulationModeCodes.MonophasicPulseCurrent, StringComparison.Ordinal))
        {
            error = "处方不是 M-tPCS 类型。";
            return false;
        }

        if (channels.Any(activeChannels.ContainsKey))
        {
            error = "目标通道正在刺激，不能应用处方。";
            return false;
        }

        var validationChannel = new ChannelConfig
        {
            Name = "处方",
            CurrentMA = prescription.CurrentMilliamp.ToString("0.00", CultureInfo.InvariantCulture),
            RampUpS = prescription.DirectCurrentRampUpDurationSeconds.ToString("0.0", CultureInfo.InvariantCulture),
            RampDownS = prescription.DirectCurrentRampUpDurationSeconds.ToString("0.0", CultureInfo.InvariantCulture),
            IntervalS = prescription.DirectCurrentIntervalDurationSeconds.ToString("0.0", CultureInfo.InvariantCulture),
            DurationS = prescription.DirectCurrentTotalDurationSeconds.ToString("0.0", CultureInfo.InvariantCulture),
            SingleDurationS = (prescription.DirectCurrentRampUpDurationSeconds * 2d).ToString("0.0", CultureInfo.InvariantCulture),
            StimulationMode = "间隔",
            Polarity = "不掉转"
        };
        if (!MonophasicPulseCurrentParameterRules.TryCreateWaveform(
                validationChannel,
                out _,
                out error))
        {
            return false;
        }

        var current = prescription.CurrentMilliamp.ToString("0.00", CultureInfo.InvariantCulture);
        var ramp = prescription.DirectCurrentRampUpDurationSeconds.ToString("0.0", CultureInfo.InvariantCulture);
        var interval = prescription.DirectCurrentIntervalDurationSeconds.ToString("0.0", CultureInfo.InvariantCulture);
        var total = prescription.DirectCurrentTotalDurationSeconds.ToString("0.0", CultureInfo.InvariantCulture);
        foreach (var channel in channels)
        {
            channel.CurrentMA = current;
            channel.RampUpS = ramp;
            channel.RampDownS = ramp;
            channel.IntervalS = interval;
            channel.DurationS = total;
            channel.SingleDurationS = (prescription.DirectCurrentRampUpDurationSeconds * 2d)
                .ToString("0.0", CultureInfo.InvariantCulture);
            channel.StimulationMode = "间隔";
            channel.Polarity = "不掉转";
            channel.RemainingTime = "00:00:00";
            channel.DirectCurrentWaveform.Clear();
            channel.RefreshBindings();
        }

        appliedPrescriptionName = prescription.Name;
        OnPropertyChanged(nameof(SelectedChannels));
        error = string.Empty;
        return true;
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
            var group = CreateGroup(targets);
            var result = await stimulationEngine.StartMonophasicPulseCurrentGroupAsync(
                group,
                string.Join(" + ", targets.Select(channel => channel.Name)),
                appliedPrescriptionName,
                cancellationToken);
            var timestamp = Stopwatch.GetTimestamp();
            foreach (var channel in targets)
            {
                BeginRuntime(channel, snapshots[channel], timestamp);
            }

            HardwareOperationCompleted?.Invoke(this, result);
        }
        finally
        {
            SetStarting(targets, false);
        }
    }

    private async Task StartChannelAsync(ChannelConfig channel, CancellationToken cancellationToken)
    {
        await EnsureFreshImpedanceAsync(cancellationToken);
        var assessment = StimulationImpedanceStartPolicy.Evaluate([channel]);
        if (assessment.EligibleChannels.Count == 0)
        {
            toastService.ShowError("无法开始刺激", StimulationImpedanceStartPolicy.BuildSingleChannelBlockedMessage(channel));
            return;
        }

        if (!MonophasicPulseCurrentParameterRules.TryCreateWaveform(channel, out var snapshot, out var error))
        {
            toastService.ShowError("参数校验失败", error);
            return;
        }

        if (!userDialogService.ConfirmWarning(
                "开始刺激确认",
                $"{channel.Name}\n\n幅值：{snapshot!.CurrentMilliamp:0.00} mA\n"
                    + $"渐升/渐降：{snapshot.RampUpSeconds:0.0} s / {snapshot.RampDownSeconds:0.0} s\n"
                    + $"刺激时间：{snapshot.TotalDurationSeconds:0.0} s\n间隔时间：{snapshot.IntervalSeconds:0.0} s\n"
                    + $"阻抗：{channel.ImpedanceOhms:0.##} Ω",
                "确认并开始",
                "返回修改"))
        {
            return;
        }

        SetStarting([channel], true);
        var group = CreateGroup([channel]);
        try
        {
            var result = await stimulationEngine.StartMonophasicPulseCurrentGroupAsync(
                group,
                channel.Name,
                appliedPrescriptionName,
                cancellationToken);
            BeginRuntime(channel, snapshot, Stopwatch.GetTimestamp());
            HardwareOperationCompleted?.Invoke(this, result);
        }
        catch (Exception startException) when (startException is not OperationCanceledException)
        {
            await TryStopAfterUnconfirmedStartAsync(group, channel.Name, startException);
            throw;
        }
        finally
        {
            SetStarting([channel], false);
        }
    }

    private async Task StopChannelAsync(ChannelConfig channel, CancellationToken cancellationToken)
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
                CreateGroup([channel]),
                channel.Name,
                StimulationModeCodes.MonophasicPulseCurrent,
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
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
        RefreshCommands();
    }

    private async Task EmergencyStopAsync(CancellationToken cancellationToken)
    {
        synchronizedStartCommand.Cancel();
        startChannelCommand.Cancel();
        var running = activeChannels.Keys.ToArray();
        var stoppedAt = Stopwatch.GetTimestamp();
        var result = await stimulationEngine.EmergencyStopMonophasicPulseCurrentGroupAsync(
            CreateGroup(running),
            "用户点击急停",
            cancellationToken);
        foreach (var channel in running)
        {
            if (activeChannels.TryGetValue(channel, out var runtime))
            {
                FinalizeChannel(
                    channel,
                    Stopwatch.GetElapsedTime(runtime.StartTimestamp, stoppedAt).TotalSeconds,
                    false);
            }
        }

        waveformTimer.Stop();
        RefreshCommands();
        HardwareOperationCompleted?.Invoke(this, result);
    }

    private async void OnWaveformTimerTick(object? sender, EventArgs e)
    {
        var now = Stopwatch.GetTimestamp();
        var completed = new List<ChannelConfig>();
        foreach (var pair in activeChannels.ToArray())
        {
            var elapsed = Stopwatch.GetElapsedTime(pair.Value.StartTimestamp, now).TotalSeconds;
            pair.Key.DirectCurrentWaveform.UpdateElapsed(elapsed);
            pair.Key.RemainingTime = FormatRemaining(pair.Value.Parameters.TotalDurationSeconds - elapsed);
            if (elapsed >= pair.Value.Parameters.TotalDurationSeconds
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

        try
        {
            var result = await stimulationEngine.CompleteGroupAsync(
                CreateGroup(completed),
                string.Join(" + ", completed.Select(channel => channel.Name)),
                StimulationModeCodes.MonophasicPulseCurrent);
            foreach (var channel in completed)
            {
                var duration = activeChannels.GetValueOrDefault(channel)?.Parameters.TotalDurationSeconds ?? 0;
                FinalizeChannel(channel, duration, true);
            }

            HardwareOperationCompleted?.Invoke(this, result);
        }
        catch (Exception exception)
        {
            logger.Error("M-tPCS自然结束停止失败", exception);
            foreach (var channel in completed)
            {
                completionPendingChannels.Remove(channel);
                completionStopFailedChannels.Add(channel);
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
        RefreshCommands();
    }

    private void BeginRuntime(ChannelConfig channel, DirectCurrentWaveformParameters parameters, long timestamp)
    {
        channel.RampDownS = channel.RampUpS;
        channel.SingleDurationS = (parameters.RampUpSeconds * 2d).ToString("0.0", CultureInfo.InvariantCulture);
        channel.DirectCurrentWaveform.Start(parameters);
        channel.RemainingTime = FormatRemaining(parameters.TotalDurationSeconds);
        channel.IsParameterEditingEnabled = false;
        channel.IsStimulating = true;
        activeChannels[channel] = new ChannelRuntime(timestamp, parameters);
        completionPendingChannels.Remove(channel);
        completionStopFailedChannels.Remove(channel);
        impedanceStopPendingChannels.Remove(channel);
        if (!waveformTimer.IsEnabled)
        {
            waveformTimer.Start();
        }

        RefreshCommands();
    }

    private void FinalizeChannel(ChannelConfig channel, double elapsedSeconds, bool completed)
    {
        activeChannels.Remove(channel);
        completionPendingChannels.Remove(channel);
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
        var unsafeChannels = new List<ChannelConfig>();
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

        RefreshCommands();
    }

    private async Task StopUnsafeChannelsAsync(IReadOnlyList<ChannelConfig> channels)
    {
        try
        {
            var result = await stimulationEngine.StopGroupAsync(
                CreateGroup(channels),
                string.Join(" + ", channels.Select(channel => channel.Name)),
                StimulationModeCodes.MonophasicPulseCurrent);
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
            }

            logger.Error("M-tPCS阻抗安全停止失败", exception);
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
        RefreshCommands();
    }

    private bool TryCreateSnapshots(
        IEnumerable<ChannelConfig> channels,
        out Dictionary<ChannelConfig, DirectCurrentWaveformParameters> snapshots)
    {
        snapshots = [];
        foreach (var channel in channels)
        {
            if (!MonophasicPulseCurrentParameterRules.TryCreateWaveform(channel, out var snapshot, out var error))
            {
                toastService.ShowError("参数校验失败", error);
                snapshots.Clear();
                return false;
            }

            snapshots[channel] = snapshot!;
        }

        return true;
    }

    private async Task TryStopAfterUnconfirmedStartAsync(TiGroup group, string channelName, Exception startException)
    {
        try
        {
            _ = await stimulationEngine.StopGroupAsync(
                group,
                channelName,
                StimulationModeCodes.MonophasicPulseCurrent,
                CancellationToken.None);
        }
        catch (Exception stopException)
        {
            logger.Error("M-tPCS启动与安全停止均未确认", new AggregateException(startException, stopException));
            toastService.ShowError("启动状态不确定", $"{channelName}启动未确认，且安全停止也未确认。请立即点击紧急停止。");
        }
    }

    private void RequestPrescription(StimulationPrescriptionApplyScope scope, object? target = null) =>
        PrescriptionRequested?.Invoke(this, new StimulationPrescriptionRequestEventArgs(
            StimulationModeCodes.MonophasicPulseCurrent,
            scope,
            target));

    private void RequestBack()
    {
        if (activeChannels.Count > 0 || startOperationInProgress)
        {
            toastService.ShowInformation("刺激正在运行或启动中，请停止或紧急停止后再离开当前界面。", "无法离开");
            return;
        }

        BackRequested?.Invoke(this, EventArgs.Empty);
    }

    private void SelectChannel(object? parameter)
    {
        if (parameter is not ChannelConfig channel || !Channels.Contains(channel))
        {
            return;
        }

        SelectedChannelPair = ChannelPairs.First(pair => pair.Channels.Contains(channel));
        SelectedChannel = channel;
    }

    private void SetStarting(IEnumerable<ChannelConfig> channels, bool value)
    {
        startOperationInProgress = value;
        foreach (var channel in channels)
        {
            channel.IsStarting = value;
        }

        RefreshCommands();
    }

    private void OnConnectionChanged(object? sender, HardwareConnectionChangedEventArgs e) => RefreshCommandsOnUiThread();
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
            _ = dispatcher.BeginInvoke(RefreshCommands);
        }
        else
        {
            RefreshCommands();
        }
    }

    private void RefreshCommands()
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
        logger.Error("M-tPCS启动失败", exception);
        toastService.ShowError("刺激启动失败", "启动命令未完成，软件未进入运行状态。请检查日志和设备状态。");
    }

    private void HandleStopFailure(Exception exception)
    {
        logger.Error("M-tPCS停止失败", exception);
        toastService.ShowError("刺激停止失败", "停止未确认，通道仍保持运行状态。请再次停止或使用紧急停止。");
    }

    private void HandleEmergencyStopFailure(Exception exception)
    {
        logger.Error("M-tPCS背板急停失败", exception);
        toastService.ShowError("紧急停止失败", "背板急停未确认，请立即人工检查设备并再次尝试急停。");
    }

    private bool CanStart => CanControlHardware && !startOperationInProgress;
    private bool CanControlHardware => hardwareConnectionState.IsConnected || debugHardwareSimulation.IsConnected;
    private static bool IsImpedanceEligible(ChannelConfig channel) =>
        channel.ImpedanceStatus is StimulationImpedanceStatus.Normal or StimulationImpedanceStatus.Warning;
    private static int GetLogicalBoardIndex(ChannelConfig channel) =>
        (ParseChannelNumber(channel.Name) - 1) / 8;

    private static ChannelConfig CreateChannel(string name, Brush accent) => new()
    {
        Name = name,
        CurrentMA = MonophasicPulseCurrentParameterRules.DefaultCurrentMilliamp,
        RampUpS = MonophasicPulseCurrentParameterRules.DefaultRampSeconds,
        RampDownS = MonophasicPulseCurrentParameterRules.DefaultRampSeconds,
        DurationS = MonophasicPulseCurrentParameterRules.DefaultTotalDurationSeconds,
        IntervalS = MonophasicPulseCurrentParameterRules.DefaultIntervalSeconds,
        SingleDurationS = "1.0",
        StimulationMode = "间隔",
        Polarity = "不掉转",
        AccentBrush = accent
    };

    private static TiGroup CreateGroup(IEnumerable<ChannelConfig> channels)
    {
        var group = new TiGroup { Title = "经颅单相脉冲电流刺激" };
        foreach (var channel in channels)
        {
            group.Channels.Add(channel);
        }

        return group;
    }

    private static int ParseChannelNumber(string name) =>
        int.Parse(new string(name.Where(char.IsDigit).ToArray()), CultureInfo.InvariantCulture);

    private static string FormatRemaining(double seconds)
    {
        var value = Math.Max(0, (int)Math.Ceiling(seconds));
        return $"{value / 3600:00}:{value % 3600 / 60:00}:{value % 60:00}";
    }

    private sealed record ChannelRuntime(long StartTimestamp, DirectCurrentWaveformParameters Parameters);
}
