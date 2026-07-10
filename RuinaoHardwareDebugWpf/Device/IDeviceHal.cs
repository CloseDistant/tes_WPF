namespace RuinaoHardwareDebugWpf;

/// <summary>
/// 设备状态快照，由 HAL 层向上层提供。
/// </summary>
public sealed record DeviceStatusSnapshot(
    DeviceConnectionState ConnectionState,
    string FirmwareVersion,
    DateTimeOffset Timestamp);

/// <summary>
/// 硬件抽象层接口。
/// 后续真实 USB3.0、WinUSB、DLL 或 TCP 实现都应收敛到这个接口后再给业务层调用。
/// </summary>
public interface IDeviceHal
{
    /// <summary>当前连接状态。</summary>
    DeviceConnectionState ConnectionState { get; }

    /// <summary>建立硬件连接。</summary>
    Task ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>断开硬件连接。</summary>
    Task DisconnectAsync(CancellationToken cancellationToken = default);

    /// <summary>读取设备整体状态。</summary>
    Task<DeviceStatusSnapshot> GetStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>读取所有可用通道的阻抗。</summary>
    Task<IReadOnlyList<ImpedanceMeasurement>> GetImpedancesAsync(CancellationToken cancellationToken = default);

    /// <summary>硬件级急停入口。</summary>
    Task EmergencyStopAsync(string reason, CancellationToken cancellationToken = default);
}
