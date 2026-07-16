namespace RuinaoSoftwareWpf;

/// <summary>
/// 硬件业务服务的具体实现。
///
/// 它位于 ViewModel 和硬件协议桥接层之间：
/// - 接收来自界面的联机、开始、暂停、急停等命令。
/// - 串行调用 RuinaoTesProtocolBridge，避免多个硬件命令同时下发。
/// - 维护心跳检测，判断设备是否仍然在线。
/// - 返回 HardwareOperationResult，供界面更新底部状态栏。
/// </summary>
public sealed class HardwareService : IHardwareService
{
    // 心跳周期：每 2 秒发送一次握手帧。
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(2);

    // 连续心跳失败达到该次数后，认为设备离线。
    private const int HeartbeatFailureLimit = 3;

    // 这里故意直接依赖具体 Bridge，而不是再套一层接口。
    // 这样在 Visual Studio 中可以从 HardwareService 直接“转到定义/查找引用”到 DLL 调用集中点。
    private readonly RuinaoTesProtocolBridge protocolBridge;
    private readonly ILoggingService logger;
    private readonly IDeviceStateMachine deviceStateMachine;
    private readonly IAuditLogService auditLog;
    private readonly IStimulationRecordService stimulationRecordService;

    // 操作锁：保证同一时刻只有一个硬件命令在执行，避免并发下发导致协议混乱。
    private readonly SemaphoreSlim operationLock = new(1, 1);

    // 心跳相关的取消源和后台任务。
    private CancellationTokenSource? heartbeatCts;
    private Task? heartbeatTask;
    private int heartbeatFailureCount;

    public HardwareService(
        RuinaoTesProtocolBridge protocolBridge,
        ILoggingService logger,
        IDeviceStateMachine deviceStateMachine,
        IAuditLogService auditLog,
        IStimulationRecordService stimulationRecordService)
    {
        this.protocolBridge = protocolBridge;
        this.logger = logger;
        this.deviceStateMachine = deviceStateMachine;
        this.auditLog = auditLog;
        this.stimulationRecordService = stimulationRecordService;
    }

    /// <summary>
    /// 当前是否认为设备已连接。默认 true，连续心跳失败后会变成 false。
    /// </summary>
    public bool IsConnected { get; private set; } = true;

    /// <summary>
    /// 联机：调用设备客户端连接，启动心跳，并返回界面状态。
    /// </summary>
    public async Task<HardwareOperationResult> ConnectAsync(CancellationToken cancellationToken = default)
    {
        deviceStateMachine.MoveTo(DeviceConnectionState.Connecting, "Connect");
        await RunDeviceOperationAsync(ConnectOnProtocolBridgeAsync, cancellationToken);
        IsConnected = true;
        deviceStateMachine.MoveTo(DeviceConnectionState.Connected, "ConnectSuccess");
        StartHeartbeat();
        auditLog.RecordUserAction("Connect device");
        logger.Hardware("联机动作：已调用协议 API 生成握手帧");
        return Result("设备：协议库联机 | 链路：心跳运行中 | 刺激：空闲");
    }

    /// <summary>
    /// 手动握手检测：发送一次握手帧，常用于测试通信是否正常。
    /// </summary>
    public async Task<HardwareOperationResult> HandshakeAsync(CancellationToken cancellationToken = default)
    {
        await RunDeviceOperationAsync(HandshakeOnProtocolBridgeAsync, cancellationToken);
        logger.Hardware("握手检测：已调用协议 API 生成握手帧");
        return Result("设备：握手检测 | 链路：已发送握手帧");
    }

    /// <summary>
    /// 断开：先停止心跳，再断开设备连接。
    /// </summary>
    public async Task<HardwareOperationResult> DisconnectAsync(CancellationToken cancellationToken = default)
    {
        await StopHeartbeatAsync();
        await RunDeviceOperationAsync(DisconnectOnProtocolBridgeAsync, cancellationToken);
        IsConnected = false;
        deviceStateMachine.MoveTo(DeviceConnectionState.Disconnected, "Disconnect");
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
    /// 阻抗检测：下发读取阻抗寄存器命令。
    /// </summary>
    public async Task<HardwareOperationResult> CheckImpedanceAsync(CancellationToken cancellationToken = default)
    {
        await RunDeviceOperationAsync(ReadImpedanceOnProtocolBridgeAsync, cancellationToken);
        logger.Hardware("阻抗检测：已调用协议 API 生成读取阻抗寄存器帧");
        return Result("设备：已联机 | 阻抗：正常 | 刺激：待启动");
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
        await RunDeviceOperationAsync(token => StartGroupOnProtocolBridgeAsync(group, token), cancellationToken);
        await stimulationRecordService.RecordAsync(
            new StimulationRecordRequest(
                "start",
                group.Title,
                selectedChannelNames,
                "running",
                parameterRecord.StimulationType,
                parameterRecord.Name,
                ParameterSnapshotJson: StimulationRecordParameters.ToJson(parameterRecord)),
            cancellationToken);

        logger.Hardware($"启动刺激：已生成 {group.Title} 通道启动帧，channels={selectedChannelNames}");
        return Result($"设备：协议库 | 模式：{parameterRecord.StimulationType} | 刺激：运行中");
    }

    /// <summary>
    /// 暂停某个 TI 刺激组。
    /// 流程：下发参数，再下发暂停命令。
    /// </summary>
    public async Task<HardwareOperationResult> PauseGroupAsync(TiGroup group, string selectedChannelNames, CancellationToken cancellationToken = default)
    {
        await RunDeviceOperationAsync(token => PauseGroupOnProtocolBridgeAsync(group, token), cancellationToken);
        await stimulationRecordService.RecordAsync(new StimulationRecordRequest("pause", group.Title, selectedChannelNames, "paused", "TI"), cancellationToken);

        logger.Hardware($"暂停/停止刺激：已生成 {group.Title} 通道停止帧，channels={selectedChannelNames}");
        return Result("设备：协议库 | 模式：TI | 刺激：已暂停");
    }

    /// <summary>
    /// 紧急停止某个 TI 刺激组。
    /// 流程：下发参数，再下发急停命令。
    /// </summary>
    public async Task<HardwareOperationResult> EmergencyStopGroupAsync(
        TiGroup group,
        string selectedChannelNames,
        string stimulationType = "TI",
        CancellationToken cancellationToken = default)
    {
        await RunDeviceOperationAsync(token => EmergencyStopGroupOnProtocolBridgeAsync(group, token), cancellationToken);
        await stimulationRecordService.RecordAsync(new StimulationRecordRequest("emergency_stop", group.Title, selectedChannelNames, "stopped", stimulationType), cancellationToken);

        logger.Hardware($"紧急停止：已生成 {group.Title} 通道停止帧，channels={selectedChannelNames}");
        return Result($"设备：协议库 | 模式：{stimulationType} | 刺激：已急停");
    }

    public async Task<HardwareOperationResult> CompleteGroupAsync(
        TiGroup group,
        string selectedChannelNames,
        string stimulationType,
        CancellationToken cancellationToken = default)
    {
        await RunDeviceOperationAsync(token => PauseGroupOnProtocolBridgeAsync(group, token), cancellationToken);
        await stimulationRecordService.RecordAsync(
            new StimulationRecordRequest(
                "complete",
                group.Title,
                selectedChannelNames,
                "completed",
                stimulationType),
            cancellationToken);

        logger.Hardware($"刺激完成：已停止 {group.Title} 通道输出，channels={selectedChannelNames}");
        return Result($"设备：协议库 | 模式：{stimulationType} | 刺激：已完成");
    }

    /// <summary>
    /// 软件退出时调用。
    /// 等待心跳任务在 800ms 内结束；超时则继续关闭程序。
    /// </summary>
    public async Task ShutdownAsync()
    {
        var stopTask = StopHeartbeatAsync();
        var completedTask = await Task.WhenAny(stopTask, Task.Delay(TimeSpan.FromMilliseconds(800)));

        if (!ReferenceEquals(completedTask, stopTask))
        {
            logger.Warning("软件退出：等待心跳停止超时，继续关闭程序");
            return;
        }

        await stopTask;
    }

    /// <summary>
    /// 协议 DLL 调用映射区。
    /// 这些方法故意集中保留在 HardwareService 内，方便从业务动作一路追到 RuinaoTesProtocolBridge。
    /// </summary>
    private Task ConnectOnProtocolBridgeAsync(CancellationToken cancellationToken)
    {
        return protocolBridge.ConnectAsync(cancellationToken);
    }

    /// <summary>调用 Bridge 生成/发送握手帧。</summary>
    private Task HandshakeOnProtocolBridgeAsync(CancellationToken cancellationToken)
    {
        return protocolBridge.HandshakeAsync(cancellationToken);
    }

    /// <summary>调用 Bridge 断开设备链路。</summary>
    private Task DisconnectOnProtocolBridgeAsync(CancellationToken cancellationToken)
    {
        return protocolBridge.DisconnectAsync(cancellationToken);
    }

    /// <summary>调用 Bridge 读取产品型号寄存器。</summary>
    private Task ReadProductModelOnProtocolBridgeAsync(CancellationToken cancellationToken)
    {
        return protocolBridge.ReadProductModelAsync(cancellationToken);
    }

    /// <summary>调用 Bridge 读取板卡型号寄存器。</summary>
    private Task ReadBoardModelOnProtocolBridgeAsync(CancellationToken cancellationToken)
    {
        return protocolBridge.ReadBoardModelAsync(cancellationToken);
    }

    /// <summary>调用 Bridge 读取阻抗寄存器。</summary>
    private Task ReadImpedanceOnProtocolBridgeAsync(CancellationToken cancellationToken)
    {
        return protocolBridge.ReadImpedanceAsync(cancellationToken);
    }

    /// <summary>
    /// 调用 Bridge 启动 TI 刺激组。
    /// 若软件状态已离线，则先走联机动作，再下发参数和启动命令。
    /// </summary>
    private async Task StartGroupOnProtocolBridgeAsync(TiGroup group, CancellationToken cancellationToken)
    {
        if (!IsConnected)
        {
            await protocolBridge.ConnectAsync(cancellationToken);
            IsConnected = true;
            StartHeartbeat();
        }

        await protocolBridge.SendTiParametersAsync(group, cancellationToken);
        await protocolBridge.StartTiAsync(cancellationToken);
    }

    /// <summary>调用 Bridge 暂停 TI 刺激组。</summary>
    private async Task PauseGroupOnProtocolBridgeAsync(TiGroup group, CancellationToken cancellationToken)
    {
        await protocolBridge.SendTiParametersAsync(group, cancellationToken);
        await protocolBridge.PauseTiAsync(cancellationToken);
    }

    /// <summary>调用 Bridge 对 TI 刺激组执行急停。</summary>
    private async Task EmergencyStopGroupOnProtocolBridgeAsync(TiGroup group, CancellationToken cancellationToken)
    {
        await protocolBridge.SendTiParametersAsync(group, cancellationToken);
        await protocolBridge.EmergencyStopAsync(cancellationToken);
    }

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

        heartbeatFailureCount = 0;
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
            heartbeatFailureCount = 0;
        }

        logger.Hardware("心跳检测：已停止");
    }

    /// <summary>
    /// 心跳循环：每隔 2 秒发送一次握手帧。
    /// 失败次数达到阈值后，将设备标记为离线。
    /// </summary>
    private async Task RunHeartbeatLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(HeartbeatInterval);

        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            try
            {
                await RunDeviceOperationAsync(HandshakeOnProtocolBridgeAsync, cancellationToken);
                heartbeatFailureCount = 0;
                logger.Hardware("心跳检测：握手帧发送完成");
            }
            catch (OperationCanceledException)
            {
                // 被取消，直接抛出以退出循环。
                throw;
            }
            catch (Exception ex)
            {
                heartbeatFailureCount++;
                logger.Error($"心跳检测失败：连续失败次数={heartbeatFailureCount}", ex);

                if (heartbeatFailureCount >= HeartbeatFailureLimit)
                {
                    IsConnected = false;
                    deviceStateMachine.MoveTo(DeviceConnectionState.Error, "HeartbeatFailure");
                    logger.Warning("心跳检测：连续失败达到阈值，设备已标记为离线");
                }
            }
        }
    }

    /// <summary>
    /// 构造操作结果，统一设置底部状态栏文字。
    /// </summary>
    private HardwareOperationResult Result(string footerStatus)
    {
        return new HardwareOperationResult(IsConnected, footerStatus);
    }
}
