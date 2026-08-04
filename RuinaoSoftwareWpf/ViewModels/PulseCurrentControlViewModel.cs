using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace RuinaoSoftwareWpf;

/// <summary>
/// tPCS 参数编辑与 DEBUG 模拟波形运行页面。真实硬件协议接入前不会向设备发送 tPCS 命令。
/// </summary>
public sealed class PulseCurrentControlViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan EmergencyStopCooldown = TimeSpan.FromSeconds(2);
    private readonly IHardwareConnectionState hardwareConnectionState;
    private readonly IToastService toastService;
    private readonly ILoggingService logger;
    private readonly IUserDialogService userDialogService;
    private readonly IStimulationRecordService? stimulationRecordService;
    private readonly IStimulationEngine? stimulationEngine;
    private readonly DispatcherTimer waveformTimer;
    private readonly Dictionary<PulseCurrentChannelConfig, ChannelRuntime> activeChannels = [];
    private readonly AsyncRelayCommand synchronizedStartCommand;
    private readonly AsyncRelayCommand startChannelCommand;
    private readonly AsyncRelayCommand stopChannelCommand;
    private readonly AsyncRelayCommand emergencyStopCommand;
    private readonly RelayCommand usePrescriptionCommand;
    private readonly RelayCommand useChannelPrescriptionCommand;
    private PulseCurrentChannelPair? selectedChannelPair;
    private PulseCurrentChannelConfig? selectedChannel;
    private string appliedPrescriptionName = "手动设置";
    private Task connectionLossRecordTask = Task.CompletedTask;
    private bool emergencyStopCooldownInProgress;
    private bool disposed;

    public PulseCurrentControlViewModel(
        IHardwareConnectionState hardwareConnectionState,
        LocalizationViewModel localization,
        IToastService toastService,
        ILoggingService logger,
        IUserDialogService userDialogService,
        IStimulationRecordService? stimulationRecordService = null,
        IStimulationEngine? stimulationEngine = null)
    {
        this.hardwareConnectionState = hardwareConnectionState;
        this.toastService = toastService;
        this.logger = logger;
        this.userDialogService = userDialogService;
        this.stimulationRecordService = stimulationRecordService;
        this.stimulationEngine = stimulationEngine;
        Localization = localization;
        Channels = new ObservableCollection<PulseCurrentChannelConfig>(
            Enumerable.Range(1, 16).Select(channelNumber =>
                new PulseCurrentChannelConfig
                {
                    Name = $"CH {channelNumber}",
                    Polarity = PulseCurrentPolarities.NotReversed
                }));
        ChannelPairs = new ObservableCollection<PulseCurrentChannelPair>(
            Enumerable.Range(0, 8).Select(pairIndex =>
                new PulseCurrentChannelPair(
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
            if (parameter is PulseCurrentChannelConfig channel && Channels.Contains(channel))
            {
                SelectedChannelPair = ChannelPairs.First(pair => pair.Channels.Contains(channel));
                SelectedChannel = channel;
            }
        });
        synchronizedStartCommand = new AsyncRelayCommand(
            (_, cancellationToken) => StartSynchronizedAsync(cancellationToken),
            _ => CanStartSimulation && activeChannels.Count == 0,
            HandleRecordFailure);
        SynchronizedStartCommand = synchronizedStartCommand;
        startChannelCommand = new AsyncRelayCommand(
            async (parameter, cancellationToken) =>
            {
                if (parameter is PulseCurrentChannelConfig channel)
                {
                    await StartChannelAsync(channel, cancellationToken);
                }
            },
            parameter => CanStartSimulation
                && parameter is PulseCurrentChannelConfig channel
                && Channels.Contains(channel)
                && !activeChannels.ContainsKey(channel),
            HandleRecordFailure);
        StartChannelCommand = startChannelCommand;
        stopChannelCommand = new AsyncRelayCommand(
            async (parameter, cancellationToken) =>
            {
                if (parameter is PulseCurrentChannelConfig channel)
                {
                    await StopChannelAsync(channel, cancellationToken);
                }
            },
            parameter => !emergencyStopCooldownInProgress
                && parameter is PulseCurrentChannelConfig channel
                && activeChannels.ContainsKey(channel),
            HandleRecordFailure);
        StopChannelCommand = stopChannelCommand;
        emergencyStopCommand = new AsyncRelayCommand(
            (_, cancellationToken) => EmergencyStopAsync(cancellationToken),
            _ => hardwareConnectionState.IsConnected && !emergencyStopCooldownInProgress,
            HandleEmergencyStopFailure);
        EmergencyStopCommand = emergencyStopCommand;
        usePrescriptionCommand = new RelayCommand(
            _ => RequestPrescription(StimulationPrescriptionApplyScope.AllChannels),
            _ => activeChannels.Count == 0 && !emergencyStopCooldownInProgress);
        UsePrescriptionCommand = usePrescriptionCommand;
        useChannelPrescriptionCommand = new RelayCommand(
            parameter => RequestPrescription(StimulationPrescriptionApplyScope.SingleChannel, parameter),
            parameter => parameter is PulseCurrentChannelConfig channel
                && Channels.Contains(channel)
                && !activeChannels.ContainsKey(channel)
                && !emergencyStopCooldownInProgress);
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

        hardwareConnectionState.ConnectionChanged += OnHardwareConnectionChanged;
        SelectedChannelPair = ChannelPairs[0];
        SelectedChannel = Channels[0];
    }

    public LocalizationViewModel Localization { get; }

    public ObservableCollection<PulseCurrentChannelConfig> Channels { get; }

    public ObservableCollection<PulseCurrentChannelPair> ChannelPairs { get; }

    public IReadOnlyList<string> Polarities => PulseCurrentPolarities.All;

    public PulseCurrentChannelPair? SelectedChannelPair
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

    public event EventHandler? BackRequested;

    public event EventHandler<StimulationPrescriptionRequestEventArgs>? PrescriptionRequested;

    public bool TryApplyPrescription(PrescriptionDefinition prescription, out string error)
    {
        return TryApplyPrescription(prescription, Channels, out error);
    }

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

        if (targets.Any(activeChannels.ContainsKey))
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

    private void RequestPrescription(
        StimulationPrescriptionApplyScope scope,
        object? targetChannel = null)
    {
        PrescriptionRequested?.Invoke(
            this,
            new StimulationPrescriptionRequestEventArgs(
                PrescriptionDefinition.PulseCurrentStimulationType,
                scope,
                targetChannel));
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
        hardwareConnectionState.ConnectionChanged -= OnHardwareConnectionChanged;
    }

    private async Task StartSynchronizedAsync(CancellationToken cancellationToken)
    {
        var impedanceAssessment = StimulationImpedanceStartPolicy.Evaluate(Channels);
        if (impedanceAssessment.EligibleChannels.Count == 0)
        {
            toastService.ShowError("同步开始失败", "没有阻抗状态允许启动的通道。");
            return;
        }

        var synchronizedChannels = impedanceAssessment.EligibleChannels.ToArray();

        var snapshots = new Dictionary<PulseCurrentChannelConfig, PulseCurrentParameters>();
        foreach (var channel in synchronizedChannels)
        {
            if (!PulseCurrentParameters.TryCreate(channel, out var snapshot, out var error))
            {
                toastService.ShowError("参数校验失败", $"{channel.Name}：{error}");
                return;
            }

            snapshots[channel] = snapshot!;
        }

        if (!ConfirmSynchronizedStart(impedanceAssessment))
        {
            return;
        }

        if (stimulationRecordService is not null)
        {
            await stimulationRecordService.StartRunAsync(
                StimulationRecordParameters.CreatePulseRunStartRequest(
                    snapshots,
                    appliedPrescriptionName,
                    "tPCS 同步运行"),
                cancellationToken);
        }

        if (!CanStartSimulation)
        {
            await EndChannelsForConnectionLossAsync(
                synchronizedChannels.Select(channel => new StimulationChannelEndItem(channel.Name)).ToArray(),
                CancellationToken.None);
            return;
        }

        // 16 个通道全部校验成功后共享同一时间戳，避免出现部分启动。
        var sharedTimestamp = Stopwatch.GetTimestamp();
        foreach (var channel in synchronizedChannels)
        {
            BeginChannelRuntime(channel, snapshots[channel], sharedTimestamp);
        }

        logger.Info($"tPCS 展览模拟同步开始：{string.Join(" + ", synchronizedChannels.Select(channel => channel.Name))}；未向硬件输出刺激。");
    }

    private async Task StartChannelAsync(
        PulseCurrentChannelConfig channel,
        CancellationToken cancellationToken)
    {
        if (!Channels.Contains(channel) || activeChannels.ContainsKey(channel))
        {
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

        if (!PulseCurrentParameters.TryCreate(channel, out var snapshot, out var error))
        {
            toastService.ShowError("参数校验失败", $"{channel.Name}：{error}");
            return;
        }

        var snapshots = new Dictionary<PulseCurrentChannelConfig, PulseCurrentParameters>
        {
            [channel] = snapshot!
        };
        if (!ConfirmPulseCurrentStart(
                [channel],
                snapshots,
                impedanceAssessment))
        {
            return;
        }

        if (stimulationRecordService is not null)
        {
            await stimulationRecordService.StartRunAsync(
                StimulationRecordParameters.CreatePulseRunStartRequest(
                    snapshots,
                    appliedPrescriptionName,
                    channel.Name),
                cancellationToken);
        }

        if (!CanStartSimulation)
        {
            await EndChannelsForConnectionLossAsync(
                [new StimulationChannelEndItem(channel.Name)],
                CancellationToken.None);
            return;
        }

        BeginChannelRuntime(channel, snapshot!, Stopwatch.GetTimestamp());
        logger.Info($"tPCS 展览模拟开始：{channel.Name}；未向硬件输出刺激。");
    }

    private void BeginChannelRuntime(
        PulseCurrentChannelConfig channel,
        PulseCurrentParameters snapshot,
        long startTimestamp)
    {
        channel.ShowPlannedTotalCount(snapshot.PlannedTotalCount);
        channel.Waveform.Start(snapshot);
        channel.RemainingTime = FormatRemaining(snapshot.TreatmentDurationSeconds);
        channel.IsParameterEditingEnabled = false;
        channel.IsStimulating = true;
        activeChannels[channel] = new ChannelRuntime(startTimestamp, snapshot);
        if (!waveformTimer.IsEnabled)
        {
            waveformTimer.Start();
        }

        RefreshCommandStates();
    }

    private async Task EmergencyStopAsync(CancellationToken cancellationToken)
    {
        CancelPendingStartOperations();
        var stoppedAt = Stopwatch.GetTimestamp();
        var stoppedChannels = activeChannels.ToArray();
        var group = CreateExecutionGroup(stoppedChannels.Select(pair => pair.Key));
        SetEmergencyStopCooldownState(true);
        try
        {
            if (stimulationEngine is not null)
            {
                _ = await stimulationEngine.EmergencyStopPulseCurrentGroupAsync(
                    group,
                    "用户点击急停",
                    cancellationToken);
            }
            else if (stimulationRecordService is not null && stoppedChannels.Length > 0)
            {
                await stimulationRecordService.EndChannelsAsync(
                    new StimulationChannelsEndRequest(
                        PrescriptionDefinition.PulseCurrentStimulationType,
                        stoppedChannels
                            .Select(pair => new StimulationChannelEndItem(
                                pair.Key.Name,
                                pair.Key.Waveform.CompletedPulseCount))
                            .ToArray(),
                        StimulationEndType.ManualTermination,
                        StimulationEndReasonCodes.EmergencyStop),
                    cancellationToken);
            }

            foreach (var pair in stoppedChannels)
            {
                var channel = pair.Key;
                var runtime = pair.Value;
                channel.Waveform.EmergencyStop(
                    Stopwatch.GetElapsedTime(runtime.StartTimestamp, stoppedAt).TotalSeconds);
                channel.RemainingTime = "00:00:00";
                channel.IsStimulating = false;
                activeChannels.Remove(channel);
                logger.Info(
                    $"tPCS 展览模拟急停：{channel.Name}，完成次数 {channel.Waveform.CompletedPulseCount}/{runtime.Parameters.PlannedTotalCount}；未向硬件输出刺激。");
            }

            waveformTimer.Stop();
            toastService.ShowSuccess("紧急停止", "已停止运行，设备状态稳定期间请等待2秒。");
            await Task.Delay(EmergencyStopCooldown);
        }
        finally
        {
            SetEmergencyStopCooldownState(false);
        }
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

    private async Task StopChannelAsync(
        PulseCurrentChannelConfig channel,
        CancellationToken cancellationToken)
    {
        if (!activeChannels.Remove(channel, out var runtime))
        {
            return;
        }

        var elapsed = Stopwatch.GetElapsedTime(runtime.StartTimestamp, Stopwatch.GetTimestamp()).TotalSeconds;
        channel.Waveform.EmergencyStop(elapsed);
        channel.RemainingTime = "00:00:00";
        channel.IsParameterEditingEnabled = true;
        channel.IsStimulating = false;
        if (activeChannels.Count == 0)
        {
            waveformTimer.Stop();
        }

        RefreshCommandStates();
        logger.Info(
            $"tPCS 展览模拟手动停止成功：{channel.Name}，完成次数 {channel.Waveform.CompletedPulseCount}/{runtime.Parameters.PlannedTotalCount}；未向硬件输出刺激。");
        if (stimulationRecordService is not null)
        {
            await stimulationRecordService.EndChannelsAsync(
                new StimulationChannelsEndRequest(
                    PrescriptionDefinition.PulseCurrentStimulationType,
                    [new StimulationChannelEndItem(channel.Name, channel.Waveform.CompletedPulseCount)],
                    StimulationEndType.ManualTermination,
                    StimulationEndReasonCodes.ChannelStop),
                cancellationToken);
        }
    }

    private async void OnWaveformTimerTick(object? sender, EventArgs e)
    {
        if (activeChannels.Count == 0)
        {
            waveformTimer.Stop();
            return;
        }

        var now = Stopwatch.GetTimestamp();
        var completedChannels = new List<PulseCurrentChannelConfig>();
        foreach (var pair in activeChannels.ToArray())
        {
            var channel = pair.Key;
            var runtime = pair.Value;
            var elapsed = Stopwatch.GetElapsedTime(runtime.StartTimestamp, now).TotalSeconds;
            channel.Waveform.UpdateElapsed(elapsed);
            channel.RemainingTime = FormatRemaining(runtime.Parameters.TreatmentDurationSeconds - elapsed);
            if (elapsed < runtime.Parameters.TreatmentDurationSeconds)
            {
                continue;
            }

            activeChannels.Remove(channel);
            channel.Waveform.Complete();
            channel.RemainingTime = "00:00:00";
            channel.IsParameterEditingEnabled = true;
            channel.IsStimulating = false;
            completedChannels.Add(channel);
        }

        if (completedChannels.Count == 0)
        {
            return;
        }

        if (activeChannels.Count == 0)
        {
            waveformTimer.Stop();
        }

        RefreshCommandStates();
        foreach (var channel in completedChannels)
        {
            logger.Info(
                $"tPCS 展览模拟完成：{channel.Name}，完成次数 {channel.Waveform.CompletedPulseCount}/{channel.Waveform.Parameters?.PlannedTotalCount}；未向硬件输出刺激。");
        }

        if (stimulationRecordService is not null)
        {
            try
            {
                await stimulationRecordService.EndChannelsAsync(
                    new StimulationChannelsEndRequest(
                        PrescriptionDefinition.PulseCurrentStimulationType,
                        completedChannels
                            .Select(channel => new StimulationChannelEndItem(
                                channel.Name,
                                channel.Waveform.CompletedPulseCount))
                            .ToArray(),
                        StimulationEndType.NormalCompletion,
                        StimulationEndReasonCodes.DurationCompleted));
            }
            catch (Exception exception)
            {
                HandleRecordFailure(exception);
            }
        }
    }

    private void RequestBack()
    {
        if (activeChannels.Count > 0)
        {
            toastService.ShowInformation(
                "刺激正在运行，请等待刺激完成或使用紧急停止后再离开当前界面。",
                "无法离开");
            return;
        }

        BackRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnHardwareConnectionChanged(
        object? sender,
        HardwareConnectionChangedEventArgs eventArgs)
    {
        void ApplyChange()
        {
            if (!eventArgs.IsConnected)
            {
                synchronizedStartCommand.Cancel();
                startChannelCommand.Cancel();
                StopRunningChannelsForConnectionLoss();
            }

            RefreshCommandStates();
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            _ = dispatcher.BeginInvoke(ApplyChange);
            return;
        }

        ApplyChange();
    }

    private bool CanStartSimulation =>
        hardwareConnectionState.IsConnected && !emergencyStopCooldownInProgress;

    private void StopRunningChannelsForConnectionLoss()
    {
        if (activeChannels.Count == 0)
        {
            return;
        }

        var stoppedAt = Stopwatch.GetTimestamp();
        var stoppedChannels = activeChannels.ToArray();
        foreach (var pair in stoppedChannels)
        {
            var channel = pair.Key;
            var elapsed = Stopwatch.GetElapsedTime(pair.Value.StartTimestamp, stoppedAt).TotalSeconds;
            channel.Waveform.EmergencyStop(elapsed);
            channel.RemainingTime = "00:00:00";
            channel.IsParameterEditingEnabled = true;
            channel.IsStimulating = false;
            activeChannels.Remove(channel);
        }

        waveformTimer.Stop();
        RefreshCommandStates();
        logger.Warning("tPCS 展览模拟因真实USB断联而结束；未发送停止或急停硬件指令。");
        connectionLossRecordTask = RecordConnectionLossAsync(stoppedChannels);
    }

    private async Task RecordConnectionLossAsync(
        IReadOnlyList<KeyValuePair<PulseCurrentChannelConfig, ChannelRuntime>> stoppedChannels)
    {
        try
        {
            await EndChannelsForConnectionLossAsync(
                stoppedChannels
                    .Select(pair => new StimulationChannelEndItem(
                        pair.Key.Name,
                        pair.Key.Waveform.CompletedPulseCount))
                    .ToArray(),
                CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.Error("tPCS 断联结束治疗记录失败", exception);
        }
    }

    private Task EndChannelsForConnectionLossAsync(
        IReadOnlyList<StimulationChannelEndItem> channels,
        CancellationToken cancellationToken)
    {
        return stimulationRecordService is null || channels.Count == 0
            ? Task.CompletedTask
            : stimulationRecordService.EndChannelsAsync(
                new StimulationChannelsEndRequest(
                    PrescriptionDefinition.PulseCurrentStimulationType,
                    channels,
                    StimulationEndType.AbnormalTermination,
                    StimulationEndReasonCodes.DeviceDisconnected,
                    "真实USB断联，刺激运行结束"),
                cancellationToken);
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

    private bool ConfirmPulseCurrentStart(
        IReadOnlyList<PulseCurrentChannelConfig> channels,
        IReadOnlyDictionary<PulseCurrentChannelConfig, PulseCurrentParameters> snapshots,
        StimulationImpedanceStartAssessment<PulseCurrentChannelConfig> impedanceAssessment)
    {
        var requestChannels = channels
            .Select(channel =>
            {
                var parameters = snapshots[channel];
                return new PulseCurrentStartChannelConfirmation(
                    channel.Name,
                    parameters.CurrentMilliamp,
                    parameters.PulseWidthMilliseconds,
                    parameters.RiseWidthMilliseconds,
                    parameters.IntervalWidthMilliseconds,
                    parameters.TreatmentDurationSeconds,
                    parameters.Polarity,
                    parameters.PlannedTotalCount,
                    channel.ImpedanceOhms!.Value,
                    impedanceAssessment.WarningChannels.Contains(channel));
            })
            .ToArray();
        return userDialogService.ConfirmPulseCurrentStart(
            new PulseCurrentStartConfirmationRequest(false, requestChannels));
    }

    private bool ConfirmSynchronizedStart(
        StimulationImpedanceStartAssessment<PulseCurrentChannelConfig> impedanceAssessment)
    {
        var message = impedanceAssessment.RequiresConfirmation
            ? StimulationImpedanceStartPolicy.BuildConfirmationMessage(impedanceAssessment)
            : $"即将同步开始{impedanceAssessment.EligibleChannels.Count}个通道的经颅脉冲电流刺激。"
                + "\n\n请确认各通道刺激参数、阻抗和连接状态无误。";
        return userDialogService.ConfirmWarning(
            "同步开始确认",
            message,
            "确认开始",
            "取消");
    }

    private static TiGroup CreateExecutionGroup(IEnumerable<PulseCurrentChannelConfig> channels)
    {
        var group = new TiGroup { Title = "tPCS" };
        foreach (var channel in channels)
        {
            group.Channels.Add(new ChannelConfig { Name = channel.Name });
        }

        return group;
    }

    private void CancelPendingStartOperations()
    {
        synchronizedStartCommand.Cancel();
        startChannelCommand.Cancel();
    }

    private void SetEmergencyStopCooldownState(bool isActive)
    {
        emergencyStopCooldownInProgress = isActive;
        foreach (var channel in Channels)
        {
            channel.IsParameterEditingEnabled = !isActive && !activeChannels.ContainsKey(channel);
        }

        RefreshCommandStates();
    }

    private void HandleEmergencyStopFailure(Exception exception)
    {
        logger.Error("tPCS 紧急停止失败", exception);
        toastService.ShowError("紧急停止失败", "紧急停止未完成，请再次尝试。");
    }

    private void HandleRecordFailure(Exception exception)
    {
        logger.Error("tPCS 治疗记录写入失败", exception);
        toastService.ShowError("治疗记录写入失败", exception.Message);
    }

    private static string FormatRemaining(double seconds)
    {
        var wholeSeconds = Math.Max(0, (int)Math.Ceiling(seconds));
        var hours = wholeSeconds / 3600;
        var minutes = wholeSeconds % 3600 / 60;
        var remainingSeconds = wholeSeconds % 60;
        return $"{hours:00}:{minutes:00}:{remainingSeconds:00}";
    }

    private sealed record ChannelRuntime(
        long StartTimestamp,
        PulseCurrentParameters Parameters);
}
