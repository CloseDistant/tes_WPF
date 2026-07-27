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
    private readonly IDebugHardwareSimulationService debugHardwareSimulation;
    private readonly IToastService toastService;
    private readonly ILoggingService logger;
    private readonly DispatcherTimer waveformTimer;
    private readonly Dictionary<PulseCurrentChannelConfig, ChannelRuntime> activeChannels = [];
    private readonly RelayCommand synchronizedStartCommand;
    private readonly RelayCommand startChannelCommand;
    private readonly RelayCommand emergencyStopCommand;
    private readonly RelayCommand usePrescriptionCommand;
    private readonly RelayCommand useChannelPrescriptionCommand;
    private PulseCurrentChannelPair? selectedChannelPair;
    private PulseCurrentChannelConfig? selectedChannel;
    private bool disposed;

    public PulseCurrentControlViewModel(
        IDebugHardwareSimulationService debugHardwareSimulation,
        LocalizationViewModel localization,
        IToastService toastService,
        ILoggingService logger)
    {
        this.debugHardwareSimulation = debugHardwareSimulation;
        this.toastService = toastService;
        this.logger = logger;
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
        synchronizedStartCommand = new RelayCommand(
            _ => StartSynchronized(),
            _ => CanStartSimulation && activeChannels.Count == 0);
        SynchronizedStartCommand = synchronizedStartCommand;
        startChannelCommand = new RelayCommand(
            parameter =>
            {
                if (parameter is PulseCurrentChannelConfig channel)
                {
                    StartChannel(channel);
                }
            },
            parameter => CanStartSimulation
                && parameter is PulseCurrentChannelConfig channel
                && Channels.Contains(channel)
                && !activeChannels.ContainsKey(channel));
        StartChannelCommand = startChannelCommand;
        emergencyStopCommand = new RelayCommand(
            _ => EmergencyStop(),
            _ => activeChannels.Count > 0);
        EmergencyStopCommand = emergencyStopCommand;
        usePrescriptionCommand = new RelayCommand(
            _ => RequestPrescription(StimulationPrescriptionApplyScope.AllChannels),
            _ => activeChannels.Count == 0);
        UsePrescriptionCommand = usePrescriptionCommand;
        useChannelPrescriptionCommand = new RelayCommand(
            parameter => RequestPrescription(StimulationPrescriptionApplyScope.SingleChannel, parameter),
            parameter => parameter is PulseCurrentChannelConfig channel
                && Channels.Contains(channel)
                && !activeChannels.ContainsKey(channel));
        UseChannelPrescriptionCommand = useChannelPrescriptionCommand;

        debugHardwareSimulation.ConnectionChanged += OnDebugSimulationConnectionChanged;
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

    public ICommand EmergencyStopCommand { get; }

    public ICommand UsePrescriptionCommand { get; }

    public ICommand UseChannelPrescriptionCommand { get; }

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

        var currentText = prescription.CurrentMilliamp.ToString("0.##", CultureInfo.InvariantCulture);
        var treatmentDurationText = prescription.PulseTreatmentDurationSeconds!.Value.ToString(CultureInfo.InvariantCulture);
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
            channel.ClearPlannedTotalCount();
            channel.RemainingTime = "00:00:00";
            channel.Waveform.Clear();
            channel.RefreshBindings();
        }

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
        debugHardwareSimulation.ConnectionChanged -= OnDebugSimulationConnectionChanged;
    }

    private void StartSynchronized()
    {
        var synchronizedChannels = Channels.ToArray();
        if (synchronizedChannels.Length != 16)
        {
            toastService.ShowError("同步开始失败", "同步开始要求 16 个通道全部可用。");
            return;
        }

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

        // 16 个通道全部校验成功后共享同一时间戳，避免出现部分启动。
        var sharedTimestamp = Stopwatch.GetTimestamp();
        foreach (var channel in synchronizedChannels)
        {
            BeginChannelRuntime(channel, snapshots[channel], sharedTimestamp);
        }

        logger.Info($"tPCS DEBUG 模拟同步开始：{string.Join(" + ", synchronizedChannels.Select(channel => channel.Name))}");
    }

    private void StartChannel(PulseCurrentChannelConfig channel)
    {
        if (!Channels.Contains(channel) || activeChannels.ContainsKey(channel))
        {
            return;
        }

        if (!PulseCurrentParameters.TryCreate(channel, out var snapshot, out var error))
        {
            toastService.ShowError("参数校验失败", $"{channel.Name}：{error}");
            return;
        }

        BeginChannelRuntime(channel, snapshot!, Stopwatch.GetTimestamp());
        logger.Info($"tPCS DEBUG 模拟开始：{channel.Name}");
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

    private void EmergencyStop()
    {
        if (activeChannels.Count == 0)
        {
            return;
        }

        var stoppedAt = Stopwatch.GetTimestamp();
        foreach (var pair in activeChannels.ToArray())
        {
            var channel = pair.Key;
            var runtime = pair.Value;
            channel.Waveform.EmergencyStop(
                Stopwatch.GetElapsedTime(runtime.StartTimestamp, stoppedAt).TotalSeconds);
            channel.RemainingTime = "00:00:00";
            channel.IsParameterEditingEnabled = true;
            channel.IsStimulating = false;
            activeChannels.Remove(channel);
            logger.Info(
                $"tPCS DEBUG 模拟急停：{channel.Name}，完成次数 {channel.Waveform.CompletedPulseCount}/{runtime.Parameters.PlannedTotalCount}");
        }

        waveformTimer.Stop();
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
                $"tPCS DEBUG 模拟完成：{channel.Name}，完成次数 {channel.Waveform.CompletedPulseCount}/{channel.Waveform.Parameters?.PlannedTotalCount}");
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

    private void OnDebugSimulationConnectionChanged(object? sender, EventArgs e)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            _ = dispatcher.BeginInvoke(RefreshCommandStates);
            return;
        }

        RefreshCommandStates();
    }

    private bool CanStartSimulation => debugHardwareSimulation.IsConnected;

    private void RefreshCommandStates()
    {
        synchronizedStartCommand.RaiseCanExecuteChanged();
        startChannelCommand.RaiseCanExecuteChanged();
        emergencyStopCommand.RaiseCanExecuteChanged();
        usePrescriptionCommand.RaiseCanExecuteChanged();
        useChannelPrescriptionCommand.RaiseCanExecuteChanged();
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
