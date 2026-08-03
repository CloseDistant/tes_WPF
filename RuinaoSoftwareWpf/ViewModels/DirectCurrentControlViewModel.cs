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
    private readonly IStimulationEngine stimulationEngine;
    private readonly IHardwareConnectionState hardwareConnectionState;
    private readonly IDebugHardwareSimulationService debugHardwareSimulation;
    private readonly ILoggingService logger;
    private readonly IToastService toastService;
    private readonly IUserDialogService userDialogService;
    private readonly DispatcherTimer waveformTimer;
    private readonly Dictionary<ChannelConfig, ChannelRuntime> activeChannels = [];
    private readonly AsyncRelayCommand synchronizedStartCommand;
    private readonly AsyncRelayCommand startChannelCommand;
    private readonly AsyncRelayCommand stopChannelCommand;
    private readonly AsyncRelayCommand emergencyStopCommand;
    private readonly RelayCommand usePrescriptionCommand;
    private readonly RelayCommand useChannelPrescriptionCommand;
    private string appliedPrescriptionName = "手动设置";
    private DirectCurrentChannelPair? selectedChannelPair;
    private ChannelConfig? selectedChannel;

    public DirectCurrentControlViewModel(
        IStimulationEngine stimulationEngine,
        IHardwareConnectionState hardwareConnectionState,
        IDebugHardwareSimulationService debugHardwareSimulation,
        ILoggingService logger,
        LocalizationViewModel localization,
        IToastService toastService,
        IUserDialogService userDialogService)
    {
        this.stimulationEngine = stimulationEngine;
        this.hardwareConnectionState = hardwareConnectionState;
        this.debugHardwareSimulation = debugHardwareSimulation;
        this.logger = logger;
        this.toastService = toastService;
        this.userDialogService = userDialogService;
        Localization = localization;

        var accent = new SolidColorBrush(Color.FromRgb(228, 232, 239));
        accent.Freeze();
        Channels = new ObservableCollection<ChannelConfig>(
            Enumerable.Range(1, 16).Select(channelNumber =>
                CreateChannel($"CH {channelNumber}", accent)));
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
            (_, _) => StartSynchronizedAsync(),
            _ => CanStartStimulation && activeChannels.Count == 0,
            HandleStartFailure);
        SynchronizedStartCommand = synchronizedStartCommand;
        startChannelCommand = new AsyncRelayCommand(
            async (parameter, _) =>
            {
                if (parameter is ChannelConfig channel)
                {
                    await StartChannelAsync(channel);
                }
            }, parameter => CanStartStimulation
                && parameter is ChannelConfig channel
                && !activeChannels.ContainsKey(channel),
            onError: HandleStartFailure);
        StartChannelCommand = startChannelCommand;
        stopChannelCommand = new AsyncRelayCommand(
            async (parameter, cancellationToken) =>
            {
                if (parameter is ChannelConfig channel)
                {
                    await StopSimulatedChannelAsync(channel, cancellationToken);
                }
            },
            parameter => debugHardwareSimulation.IsConnected
                && parameter is ChannelConfig channel
                && activeChannels.ContainsKey(channel),
            onError: HandleStopFailure);
        StopChannelCommand = stopChannelCommand;
        emergencyStopCommand = new AsyncRelayCommand(
            (_, _) => EmergencyStopAsync(),
            _ => activeChannels.Count > 0,
            ex => logger.Error("tDCS 急停命令执行失败", ex));
        EmergencyStopCommand = emergencyStopCommand;
        usePrescriptionCommand = new RelayCommand(
            _ => RequestPrescription(StimulationPrescriptionApplyScope.AllChannels),
            _ => activeChannels.Count == 0);
        UsePrescriptionCommand = usePrescriptionCommand;
        useChannelPrescriptionCommand = new RelayCommand(
            parameter => RequestPrescription(StimulationPrescriptionApplyScope.SingleChannel, parameter),
            parameter => parameter is ChannelConfig channel
                && Channels.Contains(channel)
                && !activeChannels.ContainsKey(channel));
        UseChannelPrescriptionCommand = useChannelPrescriptionCommand;
        ParameterValidationFailedCommand = new RelayCommand(parameter =>
        {
            if (parameter is string message && !string.IsNullOrWhiteSpace(message))
            {
                toastService.Show(ToastKind.Warning, "参数已调整", message);
            }
        });
        hardwareConnectionState.ConnectionChanged += OnHardwareConnectionChanged;
        debugHardwareSimulation.ConnectionChanged += OnDebugSimulationConnectionChanged;
        SelectedChannelPair = ChannelPairs[0];
        SelectedChannel = Channels[0];
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

    private async Task StartSynchronizedAsync()
    {
        if (activeChannels.Count > 0)
        {
            toastService.ShowInformation("已有通道正在运行，不能执行同步开始。", "同步开始");
            return;
        }

        var impedanceAssessment = StimulationImpedanceStartPolicy.Evaluate(Channels);
        if (impedanceAssessment.EligibleChannels.Count == 0)
        {
            toastService.ShowError("同步开始失败", "没有阻抗状态允许启动的通道。");
            return;
        }

        if (impedanceAssessment.RequiresConfirmation
            && !userDialogService.ConfirmWarning(
                "阻抗状态确认",
                StimulationImpedanceStartPolicy.BuildConfirmationMessage(impedanceAssessment),
                "确认并继续",
                "取消"))
        {
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

        var group = CreateExecutionGroup(synchronizedChannels);
        var result = await stimulationEngine.StartDirectCurrentGroupAsync(
            group,
            string.Join(" + ", synchronizedChannels.Select(channel => channel.Name)),
            AppliedPrescriptionName);
        var sharedTimestamp = Stopwatch.GetTimestamp();
        foreach (var channel in synchronizedChannels)
        {
            BeginChannelRuntime(channel, snapshots[channel], sharedTimestamp);
        }

        HardwareOperationCompleted?.Invoke(this, result);
    }

    private async Task StartChannelAsync(ChannelConfig channel)
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

        var impedanceAssessment = StimulationImpedanceStartPolicy.Evaluate([channel]);
        if (impedanceAssessment.EligibleChannels.Count == 0)
        {
            toastService.ShowError(
                "无法开始刺激",
                StimulationImpedanceStartPolicy.BuildSingleChannelBlockedMessage(channel));
            return;
        }

        if (impedanceAssessment.WarningChannels.Count > 0
            && !userDialogService.ConfirmWarning(
                "阻抗状态确认",
                StimulationImpedanceStartPolicy.BuildSingleWarningConfirmationMessage(channel),
                "确认并继续",
                "取消"))
        {
            return;
        }

        if (!DirectCurrentWaveformParameters.TryCreate(channel, out var snapshot, out var error))
        {
            toastService.ShowError("参数校验失败", error);
            return;
        }

        var group = CreateExecutionGroup([channel]);
        var result = await stimulationEngine.StartDirectCurrentGroupAsync(group, channel.Name, AppliedPrescriptionName);
        BeginChannelRuntime(channel, snapshot!, Stopwatch.GetTimestamp());
        HardwareOperationCompleted?.Invoke(this, result);
    }

    private async Task EmergencyStopAsync()
    {
        var running = activeChannels.Keys.ToArray();
        if (running.Length == 0)
        {
            return;
        }

        var stoppedAt = Stopwatch.GetTimestamp();
        var group = CreateExecutionGroup(running);
        var result = await stimulationEngine.EmergencyStopDirectCurrentGroupAsync(group, "用户点击急停");
        foreach (var channel in running)
        {
            if (!activeChannels.Remove(channel, out var runtime))
            {
                continue;
            }

            channel.DirectCurrentWaveform.EmergencyStop(Stopwatch.GetElapsedTime(runtime.StartTimestamp, stoppedAt).TotalSeconds);
            channel.RemainingTime = "00:00:00";
            channel.IsParameterEditingEnabled = true;
            channel.IsStimulating = false;
        }

        StopTimerWhenIdle();
        RefreshCommandStates();
        HardwareOperationCompleted?.Invoke(this, result);
    }

    private async Task StopSimulatedChannelAsync(
        ChannelConfig channel,
        CancellationToken cancellationToken)
    {
        if (!debugHardwareSimulation.IsConnected
            || !activeChannels.TryGetValue(channel, out var runtime))
        {
            return;
        }

        var group = CreateExecutionGroup([channel]);
        var result = await stimulationEngine.StopGroupAsync(
            group,
            channel.Name,
            "tDCS",
            cancellationToken);
        activeChannels.Remove(channel);
        var elapsed = Stopwatch.GetElapsedTime(runtime.StartTimestamp, Stopwatch.GetTimestamp()).TotalSeconds;
        channel.DirectCurrentWaveform.EmergencyStop(elapsed);
        channel.RemainingTime = "00:00:00";
        channel.IsParameterEditingEnabled = true;
        channel.IsStimulating = false;
        StopTimerWhenIdle();
        RefreshCommandStates();
        logger.Info($"tDCS DEBUG 模拟手动停止成功：{channel.Name}");
        HardwareOperationCompleted?.Invoke(this, result);
    }

    private void BeginChannelRuntime(
        ChannelConfig channel,
        DirectCurrentWaveformParameters snapshot,
        long startTimestamp)
    {
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

            activeChannels.Remove(channel);
            channel.DirectCurrentWaveform.Complete();
            channel.RemainingTime = "00:00:00";
            channel.IsParameterEditingEnabled = true;
            channel.IsStimulating = false;
            completed.Add(channel);
        }

        if (completed.Count == 0)
        {
            return;
        }

        StopTimerWhenIdle();
        RefreshCommandStates();
        foreach (var channel in completed)
        {
            _ = CompleteChannelAsync(channel);
        }
    }

    private void RequestBack()
    {
        if (activeChannels.Count > 0)
        {
            toastService.ShowInformation("刺激正在运行，请等待刺激完成或使用紧急停止后再离开当前界面。", "无法离开");
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
        RefreshCommandStatesOnUiThread();
    }

    private bool CanStartStimulation =>
        hardwareConnectionState.IsConnected || debugHardwareSimulation.IsConnected;

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

    private async Task CompleteChannelAsync(ChannelConfig channel)
    {
        try
        {
            if (!Channels.Contains(channel))
            {
                return;
            }

            var group = CreateExecutionGroup([channel]);
            var result = await stimulationEngine.CompleteGroupAsync(group, channel.Name, "tDCS");
            HardwareOperationCompleted?.Invoke(this, result);
        }
        catch (Exception ex)
        {
            logger.Error($"tDCS 通道 {channel.Name} 完成记录失败", ex);
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
