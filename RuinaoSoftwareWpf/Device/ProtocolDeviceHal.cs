namespace RuinaoSoftwareWpf;

/// <summary>
/// 基于 RuinaoTesHardwareBridge 的 HAL 适配器。
///
/// SDD 文档要求业务层通过 HAL 访问硬件；当前真实 USB3.0/WinUSB 传输层尚未接入，
/// 所以 HAL 先调用 WPF 与协议 DLL 之间的 Bridge。
/// 业务命令统一由 RuinaoTesHardware.dll 提供，HAL 不处理协议帧。
/// </summary>
public sealed class ProtocolDeviceHal : IDeviceHal
{
    private readonly RuinaoTesHardwareBridge hardwareBridge;

    public ProtocolDeviceHal(RuinaoTesHardwareBridge hardwareBridge)
    {
        this.hardwareBridge = hardwareBridge;
    }

    public DeviceConnectionState ConnectionState { get; private set; } = DeviceConnectionState.Disconnected;

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        ConnectionState = DeviceConnectionState.Connecting;
        await hardwareBridge.ConnectAsync(cancellationToken);
        ConnectionState = DeviceConnectionState.Connected;
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        await hardwareBridge.DisconnectAsync(cancellationToken);
        ConnectionState = DeviceConnectionState.Disconnected;
    }

    public Task<DeviceStatusSnapshot> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new DeviceStatusSnapshot(ConnectionState, "unknown", DateTimeOffset.Now));
    }

    public async Task<IReadOnlyList<ImpedanceMeasurement>> GetImpedancesAsync(CancellationToken cancellationToken = default)
    {
        await hardwareBridge.ReadImpedanceAsync(cancellationToken);
        return Array.Empty<ImpedanceMeasurement>();
    }

    public async Task EmergencyStopAsync(string reason, CancellationToken cancellationToken = default)
    {
        await hardwareBridge.EmergencyStopAsync(cancellationToken);
    }
}
