namespace RuinaoSoftwareWpf;

using System.Globalization;
using RuinaoTesHardware;

/// <summary>
/// 硬件业务服务的具体实现。
///
/// 它位于 ViewModel 和硬件协议桥接层之间：
/// - 接收来自界面的联机、开始、停止、急停等命令。
/// - 串行调用 RuinaoTesHardwareBridge，避免多个硬件命令同时下发。
/// - 维护心跳检测，判断设备是否仍然在线。
/// - 返回 HardwareOperationResult，供界面更新底部状态栏。
/// </summary>
public sealed class HardwareService : IHardwareService
{
    // 心跳周期：每 2 秒发送一次握手帧。
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan StimulationImpedanceInterval = TimeSpan.FromSeconds(2);

    // 这里故意直接依赖具体 Bridge，而不是再套一层接口。
    // 这样在 Visual Studio 中可以从 HardwareService 直接“转到定义/查找引用”到 DLL 调用集中点。
    private readonly RuinaoTesHardwareBridge hardwareBridge;
    private readonly ILoggingService logger;
    private readonly IDeviceStateMachine deviceStateMachine;
    private readonly IAuditLogService auditLog;
    private readonly IStimulationRecordService stimulationRecordService;
    private readonly IDebugHardwareSimulationService debugHardwareSimulation;

    // 操作锁：保证同一时刻只有一个硬件命令在执行，避免并发下发导致协议混乱。
    private readonly SemaphoreSlim operationLock = new(1, 1);
    private readonly SemaphoreSlim impedanceRefreshLock = new(1, 1);
    private readonly SemaphoreSlim impedanceMonitoringSignal = new(0, 1);
    private readonly object stimulationImpedanceStateLock = new();
    private readonly Dictionary<byte, StimulationBoardImpedanceReading> stimulationBoardReadings = [];
    private readonly StimulationBoardReadFailureTracker stimulationBoardReadFailures = new();

    // 心跳相关的取消源和后台任务。
    private CancellationTokenSource? heartbeatCts;
    private Task? heartbeatTask;
    private CancellationTokenSource? impedanceMonitoringCts;
    private Task? impedanceMonitoringTask;
    private int impedanceMonitoringRequested;
    private int connectionAttemptActive;

    public event EventHandler<HardwareConnectionChangedEventArgs>? ConnectionChanged;

    public event EventHandler<DeviceTopologyChangedEventArgs>? DeviceTopologyChanged;

    public event EventHandler<StimulationImpedanceChangedEventArgs>? StimulationImpedanceChanged;

    public HardwareService(
        RuinaoTesHardwareBridge hardwareBridge,
        ILoggingService logger,
        IDeviceStateMachine deviceStateMachine,
        IAuditLogService auditLog,
        IStimulationRecordService stimulationRecordService,
        IDebugHardwareSimulationService debugHardwareSimulation)
    {
        this.hardwareBridge = hardwareBridge;
        this.logger = logger;
        this.deviceStateMachine = deviceStateMachine;
        this.auditLog = auditLog;
        this.stimulationRecordService = stimulationRecordService;
        this.debugHardwareSimulation = debugHardwareSimulation;
    }

    /// <summary>
    /// 当前是否已通过真实背板握手。只有收到并校验硬件回复后才会变成true。
    /// </summary>
    public bool IsConnected { get; private set; }

    public bool IsConnecting => Volatile.Read(ref connectionAttemptActive) != 0;

    public DeviceTopologySnapshot? CurrentDeviceTopology { get; private set; }

    public StimulationImpedanceSnapshot? CurrentStimulationImpedance { get; private set; }

    /// <summary>
    /// 联机：调用设备客户端连接，启动心跳，并返回界面状态。
    /// </summary>
    public async Task<HardwareOperationResult> ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (IsConnected)
        {
            return Result("设备：已联机", "仪器已经处于联机状态。");
        }

        if (Interlocked.CompareExchange(ref connectionAttemptActive, 1, 0) != 0)
        {
            throw new InvalidOperationException("仪器联机正在进行，请勿重复操作。");
        }

        RaiseConnectionChanged(
            HardwareConnectionChangeReason.ConnectionAttemptStarted,
            "正在连接仪器。");
        ClearDeviceTopology();
        try
        {
            deviceStateMachine.MoveTo(DeviceConnectionState.Connecting, "Connect");
            var handshake = await RunDeviceOperationAsync(ConnectOnProtocolBridgeAsync, cancellationToken);
            IsConnected = true;
            deviceStateMachine.MoveTo(DeviceConnectionState.Connected, "ConnectSuccess");
            await TryRefreshDeviceTopologyAfterConnectAsync(cancellationToken);
            StartImpedanceMonitoringWorker();
            StartHeartbeat();
            auditLog.RecordUserAction("Connect device");
            logger.Hardware($"真实联机成功：ackSeq={handshake.ResponseAckSequence}，耗时={handshake.Elapsed.TotalMilliseconds:F1}ms");
            return Result(
                $"设备：已联机 | ACK：{handshake.ResponseAckSequence} | 耗时：{handshake.Elapsed.TotalMilliseconds:F1}ms",
                FormatHandshakeFeedback("仪器联机成功", handshake));
        }
        catch
        {
            IsConnected = false;
            await StopImpedanceMonitoringWorkerAsync();
            await CloseProtocolLinkQuietlyAsync();
            deviceStateMachine.MoveTo(DeviceConnectionState.Error, "ConnectFailed");
            throw;
        }
        finally
        {
            Interlocked.Exchange(ref connectionAttemptActive, 0);
            RaiseConnectionChanged(
                IsConnected
                    ? HardwareConnectionChangeReason.Connected
                    : HardwareConnectionChangeReason.ConnectionFailed,
                IsConnected ? "仪器已联机。" : "仪器未联机。");
        }
    }

    /// <summary>
    /// 手动握手检测：只发送一次握手帧，用于诊断通信是否正常。
    /// 离线状态下即使握手成功，也不会改变为联机状态、不会启动心跳；检测完成后立即释放临时USB链路。
    /// </summary>
    public async Task<HardwareOperationResult> HandshakeAsync(CancellationToken cancellationToken = default)
    {
        if (IsConnected)
        {
            var onlineHandshake = await RunDeviceOperationAsync(HandshakeOnProtocolBridgeAsync, cancellationToken);
            auditLog.RecordUserAction("Handshake check");
            logger.Hardware(
                $"联机状态握手检测成功：ackSeq={onlineHandshake.ResponseAckSequence}，耗时={onlineHandshake.Elapsed.TotalMilliseconds:F1}ms");
            return Result(
                $"设备：已联机 | 握手成功 | ACK：{onlineHandshake.ResponseAckSequence}",
                FormatHandshakeFeedback("握手检测成功（保持联机）", onlineHandshake));
        }

        try
        {
            // 离线诊断直接调用单次握手入口，不执行正式联机专用的“预热帧 + 正式帧”流程。
            // 这里不设置IsConnected，也不调用StartHeartbeat，避免把诊断动作误当成正式联机。
            var offlineHandshake = await RunDeviceOperationAsync(HandshakeOnProtocolBridgeAsync, cancellationToken);
            auditLog.RecordUserAction("Handshake check");
            logger.Hardware(
                $"离线握手检测成功但不进入联机状态：ackSeq={offlineHandshake.ResponseAckSequence}，"
                + $"耗时={offlineHandshake.Elapsed.TotalMilliseconds:F1}ms");
            return Result(
                $"设备：未联机 | 单次握手成功 | ACK：{offlineHandshake.ResponseAckSequence}",
                FormatHandshakeFeedback("握手检测成功（未进入联机状态）", offlineHandshake));
        }
        finally
        {
            // 无论离线握手成功、超时还是被取消，都关闭本次诊断使用的临时链路。
            IsConnected = false;
            await CloseProtocolLinkQuietlyAsync();
        }
    }

    /// <summary>
    /// 断开：先停止心跳，再断开设备连接。
    /// </summary>
    public async Task<HardwareOperationResult> DisconnectAsync(CancellationToken cancellationToken = default)
    {
        await StopHeartbeatAsync();
        await StopImpedanceMonitoringWorkerAsync();
        await RunDeviceOperationAsync(DisconnectOnProtocolBridgeAsync, cancellationToken);
        IsConnected = false;
        ClearDeviceTopology();
        deviceStateMachine.MoveTo(DeviceConnectionState.Disconnected, "Disconnect");
        RaiseConnectionChanged(HardwareConnectionChangeReason.Disconnected, "仪器未联机。");
        auditLog.RecordUserAction("Disconnect device");
        logger.Hardware("设备状态：已离线");
        return Result("设备：已断开 | 模型：未加载 | 刺激：空闲");
    }

    /// <summary>
    /// 读取产品型号寄存器。
    /// </summary>
    public async Task<HardwareOperationResult> ReadProductModelAsync(CancellationToken cancellationToken = default)
    {
        await RunDeviceOperationAsync(ReadProductModelOnProtocolBridgeAsync, cancellationToken);
        logger.Hardware("读取产品型号：已调用协议 API 生成读取寄存器帧");
        return Result("设备：读取产品型号 | 请求：已发送");
    }

    public async Task<DeviceTopologySnapshot> RefreshDeviceTopologyAsync(
        CancellationToken cancellationToken = default)
    {
        if (!IsConnected)
        {
            throw new InvalidOperationException("仪器未联机，无法读取设备拓扑。");
        }

        var hardwareSnapshot = await RunDeviceOperationAsync(
            hardwareBridge.ReadDeviceTopologyAsync,
            cancellationToken);
        var snapshot = MapDeviceTopology(hardwareSnapshot);
        CurrentDeviceTopology = snapshot;
        DeviceTopologyChanged?.Invoke(this, new DeviceTopologyChangedEventArgs(snapshot));
        RetainReadingsForCurrentTopology(snapshot);
        PublishStimulationImpedanceSnapshot();
        SignalImpedanceMonitoringIfRequested();
        logger.Hardware(
            $"设备拓扑刷新成功：slotBitmap=0x{snapshot.SlotBitmap:X8}，"
            + $"插板={snapshot.Slots.Count(slot => slot.IsInserted)}，"
            + $"在线={snapshot.Slots.Count(slot => slot.IsOnline)}");
        return snapshot;
    }

    /// <summary>
    /// 读取板卡型号寄存器。
    /// </summary>
    public async Task<HardwareOperationResult> ReadBoardModelAsync(CancellationToken cancellationToken = default)
    {
        await RunDeviceOperationAsync(ReadBoardModelOnProtocolBridgeAsync, cancellationToken);
        logger.Hardware("读取板卡型号：已调用协议 API 生成读取寄存器帧");
        return Result("设备：读取板卡型号 | 请求：已发送");
    }

    /// <summary>
    /// 阻抗检测：手动读取当前拓扑中前两块在线电刺激板的8通道阻抗。
    /// </summary>
    public async Task<HardwareOperationResult> CheckImpedanceAsync(CancellationToken cancellationToken = default)
    {
        var availableChannelCount = await RefreshStimulationImpedanceAsync(
            isManualRefresh: true,
            cancellationToken);
        return Result(
            $"设备：已联机 | 阻抗：已刷新 {availableChannelCount}/16",
            $"已更新{availableChannelCount}个在线通道的阻抗值。");
    }

    public void SetStimulationImpedanceMonitoringEnabled(bool enabled)
    {
        var requested = enabled ? 1 : 0;
        var previous = Interlocked.Exchange(ref impedanceMonitoringRequested, requested);
        if (enabled && previous == 0 && impedanceMonitoringSignal.CurrentCount == 0)
        {
            impedanceMonitoringSignal.Release();
        }
    }

    /// <summary>
    /// 启动某个 TI 刺激组。
    /// 流程：如果尚未连接，则自动联机并启动心跳；然后下发参数和启动命令。
    /// </summary>
    public async Task<HardwareOperationResult> StartGroupAsync(
        TiGroup group,
        string selectedChannelNames,
        PrescriptionDefinition parameterRecord,
        CancellationToken cancellationToken = default)
    {
        var useDebugMock = ShouldUseDebugStimulationMock();
        if (useDebugMock)
        {
            logger.Debug($"DEBUG 模拟启动刺激：mode={parameterRecord.StimulationType}, group={group.Title}, channels={selectedChannelNames}");
        }
        else
        {
            if (!IsConnected)
            {
                throw new InvalidOperationException("仪器未联机，且未启用 DEBUG 模拟联机，禁止启动电刺激。");
            }

            await RunDeviceOperationAsync(
                token => IsDirectCurrent(parameterRecord.StimulationType)
                    ? StartDirectCurrentGroupOnHardwareBridgeAsync(group, token)
                    : StartGroupOnProtocolBridgeAsync(group, token),
                cancellationToken);
        }

        try
        {
            await stimulationRecordService.StartRunAsync(
                StimulationRecordParameters.CreateRunStartRequest(group, parameterRecord),
                cancellationToken);
        }
        catch (Exception recordException)
        {
            if (!useDebugMock)
            {
                try
                {
                    await RunDeviceOperationAsync(
                        token => IsDirectCurrent(parameterRecord.StimulationType)
                            ? StopDirectCurrentGroupOnHardwareBridgeAsync(group, token)
                            : StopGroupOnHardwareBridgeAsync(group, token),
                        CancellationToken.None);
                }
                catch (Exception stopException)
                {
                    throw new InvalidOperationException(
                        "硬件已确认启动，但治疗记录创建失败，且安全停止命令也未成功。请立即检查设备状态。",
                        new AggregateException(recordException, stopException));
                }
            }

            throw new InvalidOperationException(
                "治疗记录创建失败，刺激未进入软件运行状态。",
                recordException);
        }

        if (useDebugMock)
        {
            return DebugMockResult(parameterRecord.StimulationType, "运行中");
        }

        logger.Hardware($"启动刺激：硬件 ACK 已确认，group={group.Title}, channels={selectedChannelNames}");
        return Result($"设备：已确认 | 模式：{parameterRecord.StimulationType} | 刺激：运行中");
    }

    /// <summary>
    /// 停止某个刺激组。
    /// 流程：下发参数，再下发停止命令。
    /// </summary>
    public async Task<HardwareOperationResult> StopGroupAsync(
        TiGroup group,
        string selectedChannelNames,
        string stimulationType,
        CancellationToken cancellationToken = default)
    {
        var useDebugMock = ShouldUseDebugStimulationMock();
        if (!useDebugMock)
        {
            await RunDeviceOperationAsync(
                token => IsDirectCurrent(stimulationType)
                    ? StopDirectCurrentGroupOnHardwareBridgeAsync(group, token)
                    : StopGroupOnHardwareBridgeAsync(group, token),
                cancellationToken);
        }

        await stimulationRecordService.EndChannelsAsync(
            new StimulationChannelsEndRequest(
                stimulationType,
                GetChannelNames(group).Select(item => new StimulationChannelEndItem(item)).ToArray(),
                StimulationEndType.ManualTermination,
                StimulationEndReasonCodes.ChannelStop),
            cancellationToken);

        if (useDebugMock)
        {
            logger.Debug($"DEBUG 模拟停止刺激：group={group.Title}, channels={selectedChannelNames}");
            return DebugMockResult(stimulationType, "已停止");
        }

        logger.Hardware($"停止刺激：硬件 ACK 已确认，group={group.Title}, channels={selectedChannelNames}");
        return Result($"设备：已确认 | 模式：{stimulationType} | 刺激：已停止");
    }

    /// <summary>
    /// 紧急停止某个 TI 刺激组。
    /// 流程：下发参数，再下发急停命令。
    /// </summary>
    public async Task<HardwareOperationResult> EmergencyStopGroupAsync(
        TiGroup group,
        string selectedChannelNames,
        string stimulationType = StimulationModeCodes.TemporalInterference,
        CancellationToken cancellationToken = default)
    {
        var useDebugMock = ShouldUseDebugStimulationMock();
        if (!useDebugMock)
        {
            await RunDeviceOperationAsync(
                token => IsDirectCurrent(stimulationType)
                    ? hardwareBridge.EmergencyStopBackplaneAsync(token)
                    : EmergencyStopGroupOnProtocolBridgeAsync(group, token),
                cancellationToken);
        }

        var emergencyStoppedChannelNames = GetChannelNames(group);
        if (emergencyStoppedChannelNames.Length > 0)
        {
            await stimulationRecordService.EndChannelsAsync(
                new StimulationChannelsEndRequest(
                    stimulationType,
                    emergencyStoppedChannelNames.Select(item => new StimulationChannelEndItem(item)).ToArray(),
                    StimulationEndType.ManualTermination,
                    StimulationEndReasonCodes.EmergencyStop,
                    selectedChannelNames),
                cancellationToken);
        }

        if (useDebugMock)
        {
            logger.Debug($"DEBUG 模拟急停刺激：mode={stimulationType}, group={group.Title}, channels={selectedChannelNames}");
            return DebugMockResult(stimulationType, "已急停");
        }

        logger.Hardware($"紧急停止：硬件 ACK 已确认，group={group.Title}, channels={selectedChannelNames}");
        return Result($"设备：已确认 | 模式：{stimulationType} | 刺激：已急停");
    }

    public async Task<HardwareOperationResult> CompleteGroupAsync(
        TiGroup group,
        string selectedChannelNames,
        string stimulationType,
        CancellationToken cancellationToken = default)
    {
        var useDebugMock = ShouldUseDebugStimulationMock();
        if (!useDebugMock)
        {
            await RunDeviceOperationAsync(
                token => IsDirectCurrent(stimulationType)
                    ? StopDirectCurrentGroupOnHardwareBridgeAsync(group, token)
                    : StopGroupOnHardwareBridgeAsync(group, token),
                cancellationToken);
        }

        await stimulationRecordService.EndChannelsAsync(
            new StimulationChannelsEndRequest(
                stimulationType,
                GetChannelNames(group).Select(item => new StimulationChannelEndItem(item)).ToArray(),
                StimulationEndType.NormalCompletion,
                StimulationEndReasonCodes.DurationCompleted),
            cancellationToken);

        if (useDebugMock)
        {
            logger.Debug($"DEBUG 模拟完成刺激：mode={stimulationType}, group={group.Title}, channels={selectedChannelNames}");
            return DebugMockResult(stimulationType, "已完成");
        }

        logger.Hardware($"刺激完成：停止命令 ACK 已确认，group={group.Title}, channels={selectedChannelNames}");
        return Result($"设备：已确认 | 模式：{stimulationType} | 刺激：已完成");
    }

    /// <summary>
    /// 软件退出时调用。
    /// 等待心跳任务在 800ms 内结束；超时则继续关闭程序。
    /// </summary>
    public async Task ShutdownAsync()
    {
        var stopTask = Task.WhenAll(
            StopHeartbeatAsync(),
            StopImpedanceMonitoringWorkerAsync());
        var completedTask = await Task.WhenAny(stopTask, Task.Delay(TimeSpan.FromMilliseconds(800)));

        if (!ReferenceEquals(completedTask, stopTask))
        {
            logger.Warning("软件退出：等待心跳停止超时，继续关闭程序");
        }
        else
        {
            await stopTask;
        }

        await CloseProtocolLinkQuietlyAsync();
        IsConnected = false;
        ClearDeviceTopology();
        RaiseConnectionChanged(HardwareConnectionChangeReason.Shutdown, "软件退出，仪器链路已释放。");
    }

    /// <summary>
    /// 协议 DLL 调用映射区。
    /// 这些方法集中保留在 HardwareService 内，方便从业务动作追到共用硬件 DLL 的调用入口。
    /// </summary>
    private Task<BackplaneHandshakeResult> ConnectOnProtocolBridgeAsync(CancellationToken cancellationToken)
    {
        return hardwareBridge.ConnectAsync(cancellationToken);
    }

    /// <summary>调用 Bridge 生成/发送握手帧。</summary>
    private Task<BackplaneHandshakeResult> HandshakeOnProtocolBridgeAsync(CancellationToken cancellationToken)
    {
        return hardwareBridge.HandshakeAsync(cancellationToken);
    }

    /// <summary>调用 Bridge 断开设备链路。</summary>
    private Task DisconnectOnProtocolBridgeAsync(CancellationToken cancellationToken)
    {
        return hardwareBridge.DisconnectAsync(cancellationToken);
    }

    /// <summary>调用 Bridge 读取产品型号寄存器。</summary>
    private Task ReadProductModelOnProtocolBridgeAsync(CancellationToken cancellationToken)
    {
        return hardwareBridge.ReadProductModelAsync(cancellationToken);
    }

    /// <summary>调用 Bridge 读取板卡型号寄存器。</summary>
    private Task ReadBoardModelOnProtocolBridgeAsync(CancellationToken cancellationToken)
    {
        return hardwareBridge.ReadBoardModelAsync(cancellationToken);
    }

    /// <summary>
    /// 调用 Bridge 启动 TI 刺激组。调用方必须已经完成真实设备联机。
    /// </summary>
    private async Task StartGroupOnProtocolBridgeAsync(TiGroup group, CancellationToken cancellationToken)
    {
        await hardwareBridge.SendTiParametersAsync(group, cancellationToken);
        await hardwareBridge.StartTiAsync(cancellationToken);
    }

    /// <summary>调用硬件桥停止刺激组。</summary>
    private async Task StopGroupOnHardwareBridgeAsync(TiGroup group, CancellationToken cancellationToken)
    {
        await hardwareBridge.SendTiParametersAsync(group, cancellationToken);
        await hardwareBridge.StopTiAsync(cancellationToken);
    }

    /// <summary>调用 Bridge 对 TI 刺激组执行急停。</summary>
    private async Task EmergencyStopGroupOnProtocolBridgeAsync(TiGroup group, CancellationToken cancellationToken)
    {
        await hardwareBridge.SendTiParametersAsync(group, cancellationToken);
        await hardwareBridge.EmergencyStopAsync(cancellationToken);
    }

    /// <summary>
    /// tDCS启动顺序：先逐通道下发全部配置，再按业务板合并通道掩码启动。
    /// 若后续业务板启动失败，立即向背板发送紧急停止，不尝试业务板回滚。
    /// </summary>
    private async Task StartDirectCurrentGroupOnHardwareBridgeAsync(
        TiGroup group,
        CancellationToken cancellationToken)
    {
        var bindings = CreateDirectCurrentBindings(group);
        foreach (var binding in bindings)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await hardwareBridge.ConfigureDirectCurrentAsync(binding.Parameters, cancellationToken);
        }

        var startedBoardCount = 0;
        try
        {
            foreach (var board in bindings.GroupBy(binding => binding.BoardAddress))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await hardwareBridge.StartDirectCurrentChannelsAsync(
                    board.Key,
                    CombineChannelMask(board),
                    cancellationToken);
                startedBoardCount++;
            }
        }
        catch (Exception startException)
        {
            var failedBoard = bindings
                .GroupBy(binding => binding.BoardAddress)
                .Skip(startedBoardCount)
                .FirstOrDefault();
            try
            {
                if (startedBoardCount > 0)
                {
                    // 已有前序业务板确认启动后，后续业务板失败或取消时，
                    // 无法再保证多板状态一致，按约定直接执行背板紧急停止。
                    await hardwareBridge.EmergencyStopBackplaneAsync(CancellationToken.None);
                }
                else if (failedBoard is not null)
                {
                    // 第一个业务板的启动回复未确认时，只补发本次通道掩码的停止，
                    // 不扩大为全机急停，也不影响此前独立运行的其他通道。
                    await hardwareBridge.StopDirectCurrentChannelsAsync(
                        failedBoard.Key,
                        CombineChannelMask(failedBoard),
                        CancellationToken.None);
                }
            }
            catch (Exception safetyStopException)
            {
                throw new InvalidOperationException(
                    "tDCS启动未确认，随后安全停止命令也未确认。请立即人工检查设备并使用紧急停止。",
                    new AggregateException(startException, safetyStopException));
            }

            if (startException is OperationCanceledException)
            {
                throw;
            }

            throw new InvalidOperationException(
                startedBoardCount > 0
                    ? "tDCS多板启动过程中发生失败，已向背板发送紧急停止。"
                    : "tDCS启动回复未确认，已向对应业务板补发指定通道停止。",
                startException);
        }
    }

    /// <summary>按业务板合并通道掩码停止，不重新下发配置，不附加通道拉低。</summary>
    private async Task StopDirectCurrentGroupOnHardwareBridgeAsync(
        TiGroup group,
        CancellationToken cancellationToken)
    {
        var bindings = CreateDirectCurrentBindings(group);
        foreach (var board in bindings.GroupBy(binding => binding.BoardAddress))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await hardwareBridge.StopDirectCurrentChannelsAsync(
                board.Key,
                CombineChannelMask(board),
                cancellationToken);
        }
    }

    private IReadOnlyList<DirectCurrentChannelBinding> CreateDirectCurrentBindings(TiGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);
        if (group.Channels.Count == 0)
        {
            throw new InvalidOperationException("tDCS操作至少需要一个通道。");
        }

        var boards = CurrentDeviceTopology?.Slots
            .Where(slot => slot.IsInserted
                && slot.IsOnline
                && slot.BoardKind == DeviceBoardKind.Stimulation)
            .OrderBy(slot => slot.SlotIndex)
            .ThenBy(slot => slot.Address)
            .Take(2)
            .ToArray()
            ?? [];

        var bindings = new List<DirectCurrentChannelBinding>(group.Channels.Count);
        foreach (var channel in group.Channels)
        {
            var logicalChannel = ParseLogicalChannelNumber(channel.Name);
            var boardIndex = (logicalChannel - 1) / 8;
            if (boardIndex >= boards.Length)
            {
                throw new InvalidOperationException($"{channel.Name}没有映射到在线电刺激业务板。");
            }

            var physicalChannel = (logicalChannel - 1) % 8 + 1;
            var boardAddress = boards[boardIndex].Address;
            bindings.Add(new DirectCurrentChannelBinding(
                boardAddress,
                physicalChannel,
                new DirectCurrentHardwareParameters(
                    boardAddress,
                    physicalChannel,
                    ParseDecimal(channel.CurrentMA, channel.Name, "幅值"),
                    ParseDecimal(channel.RampUpS, channel.Name, "渐升时间"),
                    ParseDecimal(channel.RampDownS, channel.Name, "渐降时间"),
                    ParseDecimal(channel.DurationS, channel.Name, "刺激时间"),
                    channel.IsContinuousMode,
                    channel.IsContinuousMode ? 0 : ParseDecimal(channel.IntervalS, channel.Name, "间隔时间"),
                    channel.IsContinuousMode ? 0 : ParseDecimal(channel.SingleDurationS, channel.Name, "单次时长"),
                    string.Equals(channel.Polarity, "调转", StringComparison.Ordinal))));
        }

        return bindings;
    }

    private static uint CombineChannelMask(IEnumerable<DirectCurrentChannelBinding> bindings) =>
        bindings.Aggregate(0U, (mask, binding) => mask | (1U << (binding.PhysicalChannelNumber - 1)));

    private static int ParseLogicalChannelNumber(string channelName)
    {
        var digits = new string(channelName.Where(char.IsDigit).ToArray());
        if (!int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            || value is < 1 or > 16)
        {
            throw new InvalidOperationException($"无法识别逻辑通道名称“{channelName}”。");
        }

        return value;
    }

    private static decimal ParseDecimal(string text, string channelName, string parameterName)
    {
        if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            || decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out value))
        {
            return value;
        }

        throw new InvalidOperationException($"{channelName}的{parameterName}不是有效数值。");
    }

    private static bool IsDirectCurrent(string stimulationType) =>
        string.Equals(
            stimulationType,
            StimulationModeCodes.DirectCurrent,
            StringComparison.OrdinalIgnoreCase);

    private sealed record DirectCurrentChannelBinding(
        byte BoardAddress,
        int PhysicalChannelNumber,
        DirectCurrentHardwareParameters Parameters);

    private bool ShouldUseDebugStimulationMock()
    {
#if DEBUG
        return !IsConnected && debugHardwareSimulation.IsConnected;
#else
        return false;
#endif
    }

    private static HardwareOperationResult DebugMockResult(string stimulationType, string status)
    {
        return new HardwareOperationResult(
            true,
            $"设备：DEBUG 模拟联机 | 模式：{stimulationType} | 刺激：{status}",
            "当前为 DEBUG 模拟运行，不会向真实硬件输出刺激。");
    }

    private static string[] GetChannelNames(TiGroup group) =>
        group.Channels
            .Select(item => item.Name)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    /// <summary>
    /// 串行执行硬件操作的辅助方法。
    /// 使用 SemaphoreSlim 保证同一时刻只有一个操作在执行，避免并发冲突。
    /// </summary>
    private async Task RunDeviceOperationAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default)
    {
        await operationLock.WaitAsync(cancellationToken);
        try
        {
            await operation(cancellationToken);
        }
        finally
        {
            operationLock.Release();
        }
    }

    /// <summary>串行执行需要返回真实硬件结果的操作。</summary>
    private async Task<T> RunDeviceOperationAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        await operationLock.WaitAsync(cancellationToken);
        try
        {
            return await operation(cancellationToken);
        }
        finally
        {
            operationLock.Release();
        }
    }

    private void StartImpedanceMonitoringWorker()
    {
        if (impedanceMonitoringTask is { IsCompleted: false })
        {
            SignalImpedanceMonitoringIfRequested();
            return;
        }

        impedanceMonitoringCts?.Dispose();
        impedanceMonitoringCts = new CancellationTokenSource();
        impedanceMonitoringTask = RunImpedanceMonitoringLoopAsync(impedanceMonitoringCts.Token);
        SignalImpedanceMonitoringIfRequested();
    }

    private async Task StopImpedanceMonitoringWorkerAsync()
    {
        var cts = impedanceMonitoringCts;
        if (cts is null)
        {
            return;
        }

        impedanceMonitoringCts = null;
        cts.Cancel();
        try
        {
            if (impedanceMonitoringTask is not null)
            {
                await impedanceMonitoringTask;
            }
        }
        catch (OperationCanceledException)
        {
            // 页面离开、断联或退出时的正常取消。
        }
        finally
        {
            cts.Dispose();
            impedanceMonitoringTask = null;
        }
    }

    private async Task RunImpedanceMonitoringLoopAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Volatile.Read(ref impedanceMonitoringRequested) == 0)
            {
                await impedanceMonitoringSignal.WaitAsync(cancellationToken);
                continue;
            }

            try
            {
                _ = await RefreshStimulationImpedanceAsync(
                    isManualRefresh: false,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // 自动读取失败由每块板的连续失败计数反映到快照；不产生循环日志和Toast。
            }

            await Task.Delay(StimulationImpedanceInterval, cancellationToken);
        }
    }

    private async Task<int> RefreshStimulationImpedanceAsync(
        bool isManualRefresh,
        CancellationToken cancellationToken)
    {
        var deviceOperationLockTaken = false;
        var lockTaken = isManualRefresh
            ? await WaitForManualImpedanceRefreshAsync(cancellationToken)
            : await impedanceRefreshLock.WaitAsync(0, cancellationToken);
        if (!lockTaken)
        {
            return CurrentStimulationImpedance?.Channels.Count(channel => channel.IsAvailable) ?? 0;
        }

        try
        {
            if (!IsConnected)
            {
                throw new InvalidOperationException("仪器未联机，无法读取通道阻抗。");
            }

            if (CurrentDeviceTopology is null && isManualRefresh)
            {
                _ = await RefreshDeviceTopologyAsync(cancellationToken);
            }

            var boards = CurrentDeviceTopology?.Slots
                .Where(slot => slot.IsInserted
                    && slot.IsOnline
                    && slot.BoardKind == DeviceBoardKind.Stimulation)
                .OrderBy(slot => slot.SlotIndex)
                .ThenBy(slot => slot.Address)
                .Take(2)
                .ToArray()
                ?? [];
            if (boards.Length == 0)
            {
                PublishStimulationImpedanceSnapshot();
                throw new InvalidOperationException("当前设备拓扑中没有在线电刺激业务板。");
            }

            // 阻抗读取属于低优先级诊断操作，不进入设备命令等待队列。
            // 自动轮询在总线忙时跳过本轮；手动读取则明确告知用户稍后重试。
            deviceOperationLockTaken = await operationLock.WaitAsync(0, cancellationToken);
            if (!deviceOperationLockTaken)
            {
                if (isManualRefresh)
                {
                    throw new InvalidOperationException(
                        "设备正在执行刺激控制或其他通信，请稍后重新读取阻抗。");
                }

                return CurrentStimulationImpedance?.Channels.Count(channel => channel.IsAvailable) ?? 0;
            }

            var successfulBoardCount = 0;
            Exception? lastFailure = null;
            foreach (var board in boards)
            {
                try
                {
                    var hardwareSnapshot = await hardwareBridge.ReadStimulationBoardImpedanceAsync(
                        board.Address,
                        cancellationToken);
                    lock (stimulationImpedanceStateLock)
                    {
                        stimulationBoardReadings[board.Address] = MapBoardImpedance(hardwareSnapshot);
                        stimulationBoardReadFailures.RecordSuccess(board.Address);
                    }
                    successfulBoardCount++;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    lastFailure = exception;
                    lock (stimulationImpedanceStateLock)
                    {
                        if (stimulationBoardReadFailures.RecordFailure(board.Address))
                        {
                            stimulationBoardReadings.Remove(board.Address);
                        }
                    }
                }
            }

            PublishStimulationImpedanceSnapshot();
            if (isManualRefresh && successfulBoardCount == 0)
            {
                throw new InvalidOperationException(
                    "在线电刺激业务板阻抗读取失败，请检查设备通信后重试。",
                    lastFailure);
            }

            return CurrentStimulationImpedance?.Channels.Count(channel => channel.IsAvailable) ?? 0;
        }
        finally
        {
            if (deviceOperationLockTaken)
            {
                operationLock.Release();
            }

            impedanceRefreshLock.Release();
        }
    }

    private async Task<bool> WaitForManualImpedanceRefreshAsync(CancellationToken cancellationToken)
    {
        await impedanceRefreshLock.WaitAsync(cancellationToken);
        return true;
    }

    private static StimulationBoardImpedanceReading MapBoardImpedance(
        TesStimulationImpedanceSnapshot source) =>
        new(
            source.BoardAddress,
            source.CapturedAt,
            source.Channels
                .Select(channel => new StimulationBoardChannelReading(
                    channel.PhysicalChannelNumber,
                    channel.RegisterAddress,
                    channel.RawValue,
                    channel.ImpedanceOhms))
                .ToArray());

    private void PublishStimulationImpedanceSnapshot()
    {
        StimulationImpedanceSnapshot snapshot;
        lock (stimulationImpedanceStateLock)
        {
            snapshot = StimulationImpedanceMapper.Map(
                CurrentDeviceTopology,
                stimulationBoardReadings,
                DateTimeOffset.Now);
            CurrentStimulationImpedance = snapshot;
        }

        StimulationImpedanceChanged?.Invoke(
            this,
            new StimulationImpedanceChangedEventArgs(snapshot));
    }

    private void RetainReadingsForCurrentTopology(DeviceTopologySnapshot topology)
    {
        var onlineAddresses = topology.Slots
            .Where(slot => slot.IsInserted
                && slot.IsOnline
                && slot.BoardKind == DeviceBoardKind.Stimulation)
            .Select(slot => slot.Address)
            .ToHashSet();
        lock (stimulationImpedanceStateLock)
        {
            stimulationBoardReadFailures.Retain(onlineAddresses);
            foreach (var address in stimulationBoardReadings.Keys
                         .Where(address => !onlineAddresses.Contains(address))
                         .ToArray())
            {
                stimulationBoardReadings.Remove(address);
            }
        }
    }

    private void SignalImpedanceMonitoringIfRequested()
    {
        if (Volatile.Read(ref impedanceMonitoringRequested) != 0
            && impedanceMonitoringSignal.CurrentCount == 0)
        {
            impedanceMonitoringSignal.Release();
        }
    }

    /// <summary>
    /// 启动后台心跳任务。
    /// 如果心跳已经在运行，则直接返回，避免重复启动。
    /// </summary>
    private void StartHeartbeat()
    {
        if (heartbeatTask is { IsCompleted: false })
        {
            return;
        }

        heartbeatCts?.Dispose();
        heartbeatCts = new CancellationTokenSource();
        heartbeatTask = RunHeartbeatLoopAsync(heartbeatCts.Token);
        logger.Hardware("心跳检测：已启动，周期=2s，方式=握手帧");
    }

    /// <summary>
    /// 停止后台心跳任务并清理资源。
    /// </summary>
    private async Task StopHeartbeatAsync()
    {
        var cts = heartbeatCts;
        if (cts is null)
        {
            return;
        }

        heartbeatCts = null;
        cts.Cancel();

        try
        {
            if (heartbeatTask is not null)
            {
                await heartbeatTask;
            }
        }
        catch (OperationCanceledException)
        {
            // 正常取消，不需要处理。
        }
        finally
        {
            cts.Dispose();
            heartbeatTask = null;
        }

        logger.Hardware("心跳检测：已停止");
    }

    /// <summary>
    /// 心跳循环：每隔2秒发送一次真实握手。任意一次心跳失败即结束循环，
    /// 释放失效链路并等待用户手动重新联机，不执行自动重连。
    /// </summary>
    private async Task RunHeartbeatLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(HeartbeatInterval);

        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            try
            {
                // 心跳不进入命令等待队列；设备正在执行更高优先级操作时跳过本轮，
                // 等下一周期再握手，不能把软件内部排队时间误判为硬件超时。
                if (!await operationLock.WaitAsync(0, cancellationToken))
                {
                    continue;
                }

                BackplaneHandshakeResult handshake;
                try
                {
                    handshake = await HandshakeOnProtocolBridgeAsync(cancellationToken);
                }
                finally
                {
                    operationLock.Release();
                }

                logger.Hardware(
                    $"心跳检测成功：ackSeq={handshake.ResponseAckSequence}，耗时={handshake.Elapsed.TotalMilliseconds:F1}ms");
            }
            catch (OperationCanceledException)
            {
                // 被取消，直接抛出以退出循环。
                throw;
            }
            catch (Exception ex)
            {
                logger.Warning($"心跳握手失败，开始重新枚举目标USB设备：{ex.Message}");

                bool deviceReady;
                try
                {
                    deviceReady = await RunDeviceOperationAsync(
                        hardwareBridge.IsBackplaneDeviceReadyAsync,
                        cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception discoveryException)
                {
                    deviceReady = false;
                    logger.Error("心跳失败后重新枚举USB设备时发生异常", discoveryException);
                }

                if (deviceReady)
                {
                    // USB仍在时不把一次迟到或超时回复直接判为拔线；下一周期继续发送握手。
                    logger.Warning("目标USB设备04B4:00F1仍存在且驱动正常，保留联机状态，下一心跳周期继续检测");
                    continue;
                }

                logger.Error("心跳握手失败且未发现可用的04B4:00F1，仪器判定断联，心跳结束", ex);
                IsConnected = false;
                await StopImpedanceMonitoringWorkerAsync();
                ClearDeviceTopology();
                deviceStateMachine.MoveTo(DeviceConnectionState.Error, "HeartbeatFailure");
                await CloseProtocolLinkQuietlyAsync();
                RaiseConnectionChanged(
                    HardwareConnectionChangeReason.HeartbeatLost,
                    $"仪器通信已断开：{ex.Message}");
                return;
            }
        }
    }

    private async Task CloseProtocolLinkQuietlyAsync()
    {
        try
        {
            await hardwareBridge.DisconnectAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.Warning($"释放USB链路时发生异常：{exception.Message}");
        }
    }

    private void RaiseConnectionChanged(HardwareConnectionChangeReason reason, string message)
    {
        ConnectionChanged?.Invoke(
            this,
            new HardwareConnectionChangedEventArgs(IsConnected, IsConnecting, reason, message));
    }

    private async Task TryRefreshDeviceTopologyAfterConnectAsync(CancellationToken cancellationToken)
    {
        try
        {
            _ = await RefreshDeviceTopologyAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            // USB握手已经成功；拓扑属于附加发现能力，失败时保留联机并允许用户手动重试。
            logger.Warning($"联机成功，但首次设备拓扑扫描失败：{exception.Message}");
        }
    }

    private static DeviceTopologySnapshot MapDeviceTopology(TesDeviceTopologySnapshot source)
    {
        var slots = source.Slots
            .Select(slot => new DeviceTopologySlot(
                slot.SlotIndex,
                slot.Address,
                slot.IsInserted,
                slot.IsOnline,
                slot.BoardKind switch
                {
                    TesBusinessBoardKind.Stimulation => DeviceBoardKind.Stimulation,
                    TesBusinessBoardKind.Eeg => DeviceBoardKind.Eeg,
                    _ => DeviceBoardKind.Unknown,
                },
                slot.IdentityText,
                slot.IdentityRegisters.ToArray(),
                slot.Elapsed,
                slot.StatusMessage))
            .ToArray();
        return new DeviceTopologySnapshot(source.SlotBitmap, source.CapturedAt, slots);
    }

    private void ClearDeviceTopology()
    {
        if (CurrentDeviceTopology is not null)
        {
            CurrentDeviceTopology = null;
            DeviceTopologyChanged?.Invoke(this, new DeviceTopologyChangedEventArgs(null));
        }

        var hadImpedanceSnapshot = false;
        lock (stimulationImpedanceStateLock)
        {
            stimulationBoardReadings.Clear();
            stimulationBoardReadFailures.Clear();
            hadImpedanceSnapshot = CurrentStimulationImpedance is not null;
            CurrentStimulationImpedance = null;
        }

        if (hadImpedanceSnapshot)
        {
            StimulationImpedanceChanged?.Invoke(
                this,
                new StimulationImpedanceChangedEventArgs(null));
        }
    }

    /// <summary>
    /// 构造操作结果，统一设置底部状态栏文字。
    /// </summary>
    private HardwareOperationResult Result(string footerStatus, string? userMessage = null)
    {
        return new HardwareOperationResult(IsConnected, footerStatus, userMessage);
    }

    private static string FormatHandshakeFeedback(string title, BackplaneHandshakeResult handshake)
    {
        return $"{title}：命令=0x{handshake.ResponseCommand:X2}，ACK序列={handshake.ResponseAckSequence}，"
            + $"硬件版本=0x{handshake.ResponseVersion:X2}，耗时={handshake.Elapsed.TotalMilliseconds:F1}ms。";
    }
}
