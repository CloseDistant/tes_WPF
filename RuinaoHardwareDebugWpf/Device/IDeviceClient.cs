namespace RuinaoHardwareDebugWpf;

/// <summary>
/// 旧版硬件通讯抽象层。
///
/// 当前主链路已经改为：
/// HardwareService -> RuinaoTesProtocolBridge -> RuinaoTesProtocol.dll。
///
/// 该接口暂时保留给旧版 Mock/Client 代码和后续对比迁移使用，不作为 WPF 当前硬件调用主入口。
/// </summary>
public interface IDeviceClient
{
    /// <summary>建立连接。</summary>
    Task ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>断开连接。</summary>
    Task DisconnectAsync(CancellationToken cancellationToken = default);

    /// <summary>发送握手帧。</summary>
    Task HandshakeAsync(CancellationToken cancellationToken = default);

    /// <summary>读取产品型号寄存器。</summary>
    Task ReadProductModelAsync(CancellationToken cancellationToken = default);

    /// <summary>读取板卡型号寄存器。</summary>
    Task ReadBoardModelAsync(CancellationToken cancellationToken = default);

    /// <summary>读取阻抗寄存器。</summary>
    Task ReadImpedanceAsync(CancellationToken cancellationToken = default);

    /// <summary>启动温度采集。</summary>
    Task StartTemperatureAcquisitionAsync(ushort periodMs = 200, uint channelMask = 0, CancellationToken cancellationToken = default);

    /// <summary>停止温度采集。</summary>
    Task StopTemperatureAcquisitionAsync(uint channelMask = 0, CancellationToken cancellationToken = default);

    /// <summary>启动阻抗采集。</summary>
    Task StartImpedanceAcquisitionAsync(ushort periodMs = 200, uint channelMask = 0, CancellationToken cancellationToken = default);

    /// <summary>停止阻抗采集。</summary>
    Task StopImpedanceAcquisitionAsync(uint channelMask = 0, CancellationToken cancellationToken = default);

    /// <summary>下发 TI 刺激参数。</summary>
    Task SendParametersAsync(TiGroup group, CancellationToken cancellationToken = default);

    /// <summary>启动刺激输出。</summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>暂停刺激输出。</summary>
    Task PauseAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 急停接口。真实实现必须优先调用硬件级急停命令，并返回硬件确认结果。
    /// </summary>
    Task EmergencyStopAsync(CancellationToken cancellationToken = default);
}
