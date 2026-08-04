namespace RuinaoSoftwareWpf.Features.Exhibition.Services;

/// <summary>
/// 展览版硬件边界：真实联机、握手、心跳和拓扑继续委托给下位机；
/// 所有刺激输出和真实阻抗读取在此截断，防止任何刺激帧进入USB链路。
/// </summary>
public sealed class ExhibitionHardwareService : IHardwareService, IDisposable
{
    private static readonly TimeSpan ImpedanceRefreshInterval = TimeSpan.FromSeconds(2);
    private static readonly decimal[] ImpedanceOffsetsOhms =
        [0.18m, 0.67m, 1.24m, 0.79m, 0.31m, -0.26m, -0.83m, -0.37m];
    private const decimal FirstChannelImpedanceOhms = 500m;
    private const decimal ChannelImpedanceStepOhms = 20m;
    private const int ChannelCount = 16;

    private readonly IHardwareService inner;
    private readonly IDebugHardwareSimulationService? localConnection;
    private readonly IStimulationRecordService stimulationRecordService;
    private readonly ILoggingService logger;
    private readonly object impedanceSync = new();
    private Timer? impedanceTimer;
    private StimulationImpedanceSnapshot? currentStimulationImpedance;
    private int impedanceMonitoringEnabled;
    private int impedanceSequence;
    private int disposed;

    public ExhibitionHardwareService(
        HardwareService inner,
        IStimulationRecordService stimulationRecordService,
        ILoggingService logger,
        IExhibitionModeState exhibitionMode,
        IDebugHardwareSimulationService localConnection)
        : this((IHardwareService)inner, stimulationRecordService, logger, exhibitionMode, localConnection)
    {
    }

    internal ExhibitionHardwareService(
        IHardwareService inner,
        IStimulationRecordService stimulationRecordService,
        ILoggingService logger,
        IExhibitionModeState exhibitionMode,
        IDebugHardwareSimulationService? localConnection = null)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(stimulationRecordService);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(exhibitionMode);
        if (!exhibitionMode.IsEnabled)
        {
            throw new InvalidOperationException("展览硬件边界只能在EXHIBITION构建中启用。");
        }

        this.inner = inner;
        this.localConnection = localConnection;
        this.stimulationRecordService = stimulationRecordService;
        this.logger = logger;
        inner.ConnectionChanged += OnInnerConnectionChanged;
        inner.DeviceTopologyChanged += OnInnerDeviceTopologyChanged;
        if (localConnection is not null)
        {
            localConnection.ConnectionChanged += OnLocalConnectionChanged;
        }
    }

    public event EventHandler<HardwareConnectionChangedEventArgs>? ConnectionChanged;

    public event EventHandler<DeviceTopologyChangedEventArgs>? DeviceTopologyChanged;

    public event EventHandler<StimulationImpedanceChangedEventArgs>? StimulationImpedanceChanged;

    public bool IsConnected => inner.IsConnected || localConnection?.IsConnected == true;

    public bool IsConnecting => inner.IsConnecting;

    public DeviceTopologySnapshot? CurrentDeviceTopology => inner.CurrentDeviceTopology;

    public StimulationImpedanceSnapshot? CurrentStimulationImpedance
    {
        get
        {
            lock (impedanceSync)
            {
                return currentStimulationImpedance;
            }
        }
    }

    public async Task<HardwareOperationResult> ConnectAsync(CancellationToken cancellationToken = default)
    {
        var result = await inner.ConnectAsync(cancellationToken);
        PublishImpedanceIfMonitoring();
        return result;
    }

    public Task<HardwareOperationResult> HandshakeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return IsLocalConnectionActive
            ? Task.FromResult(ExhibitionResult("握手正常"))
            : inner.HandshakeAsync(cancellationToken);
    }

    public async Task<HardwareOperationResult> DisconnectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (IsLocalConnectionActive)
        {
            localConnection!.Disconnect();
            StopImpedanceTimer();
            ClearImpedance();
            return new HardwareOperationResult(false, "设备：未联机", "设备连接已断开。");
        }

        try
        {
            return await inner.DisconnectAsync(cancellationToken);
        }
        finally
        {
            StopImpedanceTimer();
            ClearImpedance();
        }
    }

    public Task<HardwareOperationResult> ReadProductModelAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return IsLocalConnectionActive
            ? Task.FromResult(ExhibitionResult("设备信息已就绪"))
            : inner.ReadProductModelAsync(cancellationToken);
    }

    public Task<HardwareOperationResult> ReadBoardModelAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return IsLocalConnectionActive
            ? Task.FromResult(ExhibitionResult("通道信息已就绪"))
            : inner.ReadBoardModelAsync(cancellationToken);
    }

    public Task<DeviceTopologySnapshot> RefreshDeviceTopologyAsync(
        CancellationToken cancellationToken = default) =>
        IsLocalConnectionActive
            ? Task.FromResult(new DeviceTopologySnapshot(0, DateTimeOffset.UtcNow, []))
            : inner.RefreshDeviceTopologyAsync(cancellationToken);

    public Task<HardwareOperationResult> CheckImpedanceAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureConnectedForSimulation();
        PublishNextImpedanceSnapshot();
        logger.Info("展览模拟：已刷新CH1～CH16正常阻抗，未读取真实阻抗寄存器。");
        return Task.FromResult(ExhibitionResult("阻抗已刷新 16/16"));
    }

    public void SetStimulationImpedanceMonitoringEnabled(bool enabled)
    {
        Interlocked.Exchange(ref impedanceMonitoringEnabled, enabled ? 1 : 0);
        if (!enabled || !IsConnected)
        {
            StopImpedanceTimer();
            return;
        }

        PublishNextImpedanceSnapshot();
        StartImpedanceTimer();
    }

    public async Task<HardwareOperationResult> StartGroupAsync(
        TiGroup group,
        string selectedChannelNames,
        PrescriptionDefinition parameterRecord,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(parameterRecord);
        EnsureConnectedForSimulation();
        await stimulationRecordService.StartRunAsync(
            StimulationRecordParameters.CreateRunStartRequest(group, parameterRecord),
            cancellationToken);
        if (!IsConnected)
        {
            await EndChannelsAsync(
                group,
                parameterRecord.StimulationType,
                StimulationEndType.AbnormalTermination,
                StimulationEndReasonCodes.DeviceDisconnected,
                "真实USB在启动期间断联",
                CancellationToken.None);
            throw new InvalidOperationException("真实USB已断联，刺激启动已取消。");
        }

        LogIntercepted("开始", parameterRecord.StimulationType, group, selectedChannelNames);
        return ExhibitionResult($"{parameterRecord.StimulationType} 运行中");
    }

    public async Task<HardwareOperationResult> StopGroupAsync(
        TiGroup group,
        string selectedChannelNames,
        string stimulationType,
        CancellationToken cancellationToken = default)
    {
        await EndChannelsAsync(
            group,
            stimulationType,
            StimulationEndType.ManualTermination,
            StimulationEndReasonCodes.ChannelStop,
            null,
            cancellationToken);
        LogIntercepted("停止", stimulationType, group, selectedChannelNames);
        return ExhibitionResult($"{stimulationType} 已停止");
    }

    public async Task<HardwareOperationResult> EmergencyStopGroupAsync(
        TiGroup group,
        string selectedChannelNames,
        string stimulationType = "TI",
        CancellationToken cancellationToken = default)
    {
        var deviceDisconnected = selectedChannelNames.Contains(
            "USB断联",
            StringComparison.Ordinal);
        await EndChannelsAsync(
            group,
            stimulationType,
            deviceDisconnected
                ? StimulationEndType.AbnormalTermination
                : StimulationEndType.ManualTermination,
            deviceDisconnected
                ? StimulationEndReasonCodes.DeviceDisconnected
                : StimulationEndReasonCodes.EmergencyStop,
            selectedChannelNames,
            cancellationToken);
        LogIntercepted("急停", stimulationType, group, selectedChannelNames);
        return ExhibitionResult($"{stimulationType} 已急停");
    }

    public async Task<HardwareOperationResult> CompleteGroupAsync(
        TiGroup group,
        string selectedChannelNames,
        string stimulationType,
        CancellationToken cancellationToken = default)
    {
        await EndChannelsAsync(
            group,
            stimulationType,
            StimulationEndType.NormalCompletion,
            StimulationEndReasonCodes.DurationCompleted,
            null,
            cancellationToken);
        LogIntercepted("到时完成", stimulationType, group, selectedChannelNames);
        return ExhibitionResult($"{stimulationType} 已完成");
    }

    public async Task ShutdownAsync()
    {
        StopImpedanceTimer();
        ClearImpedance();
        await inner.ShutdownAsync();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        StopImpedanceTimer();
        inner.ConnectionChanged -= OnInnerConnectionChanged;
        inner.DeviceTopologyChanged -= OnInnerDeviceTopologyChanged;
        if (localConnection is not null)
        {
            localConnection.ConnectionChanged -= OnLocalConnectionChanged;
        }
    }

    private void OnInnerConnectionChanged(
        object? sender,
        HardwareConnectionChangedEventArgs eventArgs)
    {
        if (eventArgs.IsConnected)
        {
            PublishImpedanceIfMonitoring();
        }
        else
        {
            StopImpedanceTimer();
            ClearImpedance();
        }

        ConnectionChanged?.Invoke(this, eventArgs);
    }

    private void OnInnerDeviceTopologyChanged(object? sender, DeviceTopologyChangedEventArgs eventArgs)
    {
        DeviceTopologyChanged?.Invoke(this, eventArgs);
    }

    private void OnLocalConnectionChanged(object? sender, EventArgs eventArgs)
    {
        if (localConnection?.IsConnected == true)
        {
            PublishImpedanceIfMonitoring();
        }
        else if (!inner.IsConnected)
        {
            StopImpedanceTimer();
            ClearImpedance();
        }

        ConnectionChanged?.Invoke(
            this,
            new HardwareConnectionChangedEventArgs(
                IsConnected,
                false,
                IsConnected
                    ? HardwareConnectionChangeReason.Connected
                    : HardwareConnectionChangeReason.Disconnected,
                IsConnected ? "设备已联机。" : "设备连接已断开。"));
    }

    private bool IsLocalConnectionActive =>
        localConnection?.IsConnected == true && !inner.IsConnected;

    private void PublishImpedanceIfMonitoring()
    {
        if (Volatile.Read(ref impedanceMonitoringEnabled) == 0 || !IsConnected)
        {
            return;
        }

        PublishNextImpedanceSnapshot();
        StartImpedanceTimer();
    }

    private void StartImpedanceTimer()
    {
        lock (impedanceSync)
        {
            impedanceTimer ??= new Timer(
                _ => PublishNextImpedanceSnapshot(),
                null,
                ImpedanceRefreshInterval,
                ImpedanceRefreshInterval);
        }
    }

    private void StopImpedanceTimer()
    {
        Timer? timer;
        lock (impedanceSync)
        {
            timer = impedanceTimer;
            impedanceTimer = null;
        }

        timer?.Dispose();
    }

    private void PublishNextImpedanceSnapshot()
    {
        if (!IsConnected || Volatile.Read(ref disposed) != 0)
        {
            return;
        }

        var sequence = Interlocked.Increment(ref impedanceSequence) - 1;
        var capturedAt = DateTimeOffset.Now;
        var channels = Enumerable.Range(1, ChannelCount)
            .Select(channelNumber => new StimulationImpedanceChannelSnapshot(
                channelNumber,
                BoardSlotIndex: null,
                BoardAddress: null,
                PhysicalChannelNumber: null,
                RegisterAddress: null,
                RawValue: null,
                ImpedanceOhms: FirstChannelImpedanceOhms
                    + ((channelNumber - 1) * ChannelImpedanceStepOhms)
                    + ImpedanceOffsetsOhms[
                        (sequence + channelNumber - 1) % ImpedanceOffsetsOhms.Length],
                LastSuccessfulReadAt: capturedAt))
            .ToArray();
        var snapshot = new StimulationImpedanceSnapshot(capturedAt, channels);
        lock (impedanceSync)
        {
            currentStimulationImpedance = snapshot;
        }

        StimulationImpedanceChanged?.Invoke(
            this,
            new StimulationImpedanceChangedEventArgs(snapshot));
    }

    private void ClearImpedance()
    {
        var changed = false;
        lock (impedanceSync)
        {
            changed = currentStimulationImpedance is not null;
            currentStimulationImpedance = null;
        }

        if (changed)
        {
            StimulationImpedanceChanged?.Invoke(
                this,
                new StimulationImpedanceChangedEventArgs(null));
        }
    }

    private Task EndChannelsAsync(
        TiGroup group,
        string stimulationType,
        StimulationEndType endType,
        string endReasonCode,
        string? detail,
        CancellationToken cancellationToken)
    {
        var channels = group.Channels
            .Select(channel => channel.Name)
            .Where(channelName => !string.IsNullOrWhiteSpace(channelName))
            .Distinct(StringComparer.Ordinal)
            .Select(channelName => new StimulationChannelEndItem(channelName))
            .ToArray();
        return channels.Length == 0
            ? Task.CompletedTask
            : stimulationRecordService.EndChannelsAsync(
                new StimulationChannelsEndRequest(
                    stimulationType,
                    channels,
                    endType,
                    endReasonCode,
                    detail),
                cancellationToken);
    }

    private void EnsureConnectedForSimulation()
    {
        if (!IsConnected)
        {
            throw new InvalidOperationException("真实仪器未联机，禁止开始刺激。");
        }
    }

    private void LogIntercepted(
        string operation,
        string stimulationType,
        TiGroup group,
        string selectedChannelNames)
    {
        logger.Info(
            $"展览模拟：{operation}已在应用硬件边界截断，未拼帧、未访问刺激USB接口、未向业务板或背板输出；"
            + $"mode={stimulationType}, group={group.Title}, channels={selectedChannelNames}");
    }

    private HardwareOperationResult ExhibitionResult(string state) =>
        new(
            IsConnected,
            $"设备：已联机 | 刺激：{state}",
            "运行状态已更新。");
}
