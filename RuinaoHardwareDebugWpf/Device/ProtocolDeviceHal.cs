namespace RuinaoHardwareDebugWpf;

/// <summary>
/// 基于 RuinaoTesProtocolBridge 的 HAL 适配器。
///
/// SDD 文档要求业务层通过 HAL 访问硬件；当前真实 USB3.0/WinUSB 传输层尚未接入，
/// 所以 HAL 先调用 WPF 与协议 DLL 之间的 Bridge。
/// 后续如果硬件方提供完整发送型 DLL，可以优先替换 RuinaoTesProtocolBridge。
/// </summary>
public sealed class ProtocolDeviceHal : IDeviceHal
{
    private readonly RuinaoTesProtocolBridge protocolBridge;

    public ProtocolDeviceHal(RuinaoTesProtocolBridge protocolBridge)
    {
        this.protocolBridge = protocolBridge;
    }

    public DeviceConnectionState ConnectionState { get; private set; } = DeviceConnectionState.Disconnected;

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        ConnectionState = DeviceConnectionState.Connecting;
        await protocolBridge.ConnectAsync(cancellationToken);
        ConnectionState = DeviceConnectionState.Connected;
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        await protocolBridge.DisconnectAsync(cancellationToken);
        ConnectionState = DeviceConnectionState.Disconnected;
    }

    public Task<DeviceStatusSnapshot> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new DeviceStatusSnapshot(ConnectionState, "unknown", DateTimeOffset.Now));
    }

    public async Task<IReadOnlyList<ImpedanceMeasurement>> GetImpedancesAsync(CancellationToken cancellationToken = default)
    {
        await protocolBridge.ReadImpedanceAsync(cancellationToken);
        return Array.Empty<ImpedanceMeasurement>();
    }

    public async Task EmergencyStopAsync(string reason, CancellationToken cancellationToken = default)
    {
        await protocolBridge.EmergencyStopAsync(cancellationToken);
    }
}
