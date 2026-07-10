namespace RuinaoHardwareDebugWpf;

/// <summary>
/// 硬件传输层接口。
///
/// Bridge 负责“业务命令 -> 协议 DLL -> byte[] 协议帧”，
/// Transport 负责“byte[] 协议帧 -> 真实硬件链路”。
///
/// 当前可以使用 LogOnlyHardwareTransport 只写日志；
/// 后续接入 USB3.0、WinUSB、串口、TCP 或厂商 SDK 时，只需要新增实现类并替换 DI 注册。
/// </summary>
public interface IHardwareTransport
{
    /// <summary>
    /// 发送一帧协议数据。
    /// </summary>
    /// <param name="commandName">业务命令名称，用于日志定位。</param>
    /// <param name="frame">协议 DLL 生成的完整 byte[] 帧。</param>
    /// <param name="cancellationToken">取消信号。</param>
    Task SendFrameAsync(string commandName, byte[] frame, CancellationToken cancellationToken = default);
}
