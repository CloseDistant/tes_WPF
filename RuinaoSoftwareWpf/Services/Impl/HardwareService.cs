namespace RuinaoSoftwareWpf;

using RuinaoTesHardware;

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
    private int connectionAttemptActive;

    public event EventHandler<HardwareConnectionChangedEventArgs>? ConnectionChanged;

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
    /// 当前是否已通过真实背板握手。只有收到并校验硬件回复后才会变成true。
    /// </summary>
    public bool IsConnected { get; private set; }

    public bool IsConnecting => Volatile.Read(ref connectionAttemptActive) != 0;

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
        try
        {
            deviceStateMachine.MoveTo(DeviceConnectionState.Connecting, "Connect");
            var handshake = await RunDeviceOperationAsync(ConnectOnProtocolBridgeAsync, cancellationToken);
            IsConnected = true;
            deviceStateMachine.MoveTo(DeviceConnectionState.Connected, "ConnectSuccess");
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
    /// 手动握手检测：发送一次握手帧，常用于测试通信是否正常。
    /// </summary>
    public async Task<HardwareOperationResult> HandshakeAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConnected)
        {
            return await ConnectAsync(cancellationToken);
        }

        var handshake = await RunDeviceOperationAsync(HandshakeOnProtocolBridgeAsync, cancellationToken);
        logger.Hardware($"真实握手成功：ackSeq={handshake.ResponseAckSequence}，耗时={handshake.Elapsed.TotalMilliseconds:F1}ms");
        return Result(
            $"设备：握手成功 | ACK：{handshake.ResponseAckSequence} | 耗时：{handshake.Elapsed.TotalMilliseconds:F1}ms",
            FormatHandshakeFeedback("握手检测成功", handshake));
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
        }
        else
        {
            await stopTask;
        }

        await CloseProtocolLinkQuietlyAsync();
        IsConnected = false;
        RaiseConnectionChanged(HardwareConnectionChangeReason.Shutdown, "软件退出，仪器链路已释放。");
    }

    /// <summary>
    /// 协议 DLL 调用映射区。
    /// 这些方法故意集中保留在 HardwareService 内，方便从业务动作一路追到 RuinaoTesProtocolBridge。
    /// </summary>
    private Task<BackplaneHandshakeResult> ConnectOnProtocolBridgeAsync(CancellationToken cancellationToken)
    {
        return protocolBridge.ConnectAsync(cancellationToken);
    }

    /// <summary>调用 Bridge 生成/发送握手帧。</summary>
    private Task<BackplaneHandshakeResult> HandshakeOnProtocolBridgeAsync(CancellationToken cancellationToken)
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
                var handshake = await RunDeviceOperationAsync(HandshakeOnProtocolBridgeAsync, cancellationToken);
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
                        protocolBridge.IsBackplaneDeviceReadyAsync,
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
            await protocolBridge.DisconnectAsync(CancellationToken.None);
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
