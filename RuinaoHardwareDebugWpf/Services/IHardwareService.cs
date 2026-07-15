namespace RuinaoHardwareDebugWpf;

/// <summary>
/// 硬件操作结果。
/// 它是个 record（不可变数据对象），用来告诉 ViewModel：
/// - 操作后设备是否还连着（IsConnected）
/// - 底部状态栏该显示什么文字（FooterStatus）
/// </summary>
public sealed record HardwareOperationResult(bool IsConnected, string FooterStatus);

/// <summary>
/// 硬件业务服务接口。
///
/// ViewModel 不直接操作设备，而是通过 IHardwareService 发命令。
/// 这样以后如果硬件协议变了，只要换实现类，界面代码不需要改。
/// </summary>
public interface IHardwareService
{
    /// <summary>
    /// 当前是否认为设备已连接。
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// 联机：建立与设备的通讯，并启动心跳检测。
    /// </summary>
    Task<HardwareOperationResult> ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 握手检测：发送一次握手帧，检查设备是否响应。
    /// </summary>
    Task<HardwareOperationResult> HandshakeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 断开：停止心跳，断开设备连接。
    /// </summary>
    Task<HardwareOperationResult> DisconnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 读取产品型号。
    /// </summary>
    Task<HardwareOperationResult> ReadProductModelAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 读取板卡型号。
    /// </summary>
    Task<HardwareOperationResult> ReadBoardModelAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 阻抗检测。
    /// </summary>
    Task<HardwareOperationResult> CheckImpedanceAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 启动指定 TI 组的刺激。
    /// </summary>
    Task<HardwareOperationResult> StartGroupAsync(
        TiGroup group,
        string selectedChannelNames,
        PrescriptionDefinition parameterRecord,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 暂停指定 TI 组的刺激。
    /// </summary>
    Task<HardwareOperationResult> PauseGroupAsync(TiGroup group, string selectedChannelNames, CancellationToken cancellationToken = default);

    /// <summary>
    /// 紧急停止指定 TI 组的刺激。
    /// </summary>
    Task<HardwareOperationResult> EmergencyStopGroupAsync(
        TiGroup group,
        string selectedChannelNames,
        string stimulationType = "TI",
        CancellationToken cancellationToken = default);

    /// <summary>停止已完成通道并写入自然完成记录。</summary>
    Task<HardwareOperationResult> CompleteGroupAsync(
        TiGroup group,
        string selectedChannelNames,
        string stimulationType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 软件退出时调用：优雅地停止心跳等后台任务。
    /// </summary>
    Task ShutdownAsync();
}
