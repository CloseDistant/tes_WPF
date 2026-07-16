namespace RuinaoSoftwareWpf;

/// <summary>
/// 旧版 IDeviceClient 的假设备实现（Mock）。
///
/// 它不连接真实硬件，所有方法都直接返回 Task.CompletedTask。
/// 用途：
/// - 兼容早期测试或历史对比代码。
/// - 后续如果恢复 IDeviceClient 测试链路，可以用它代替真实设备。
///
/// 当前主链路已经改为 HardwareService -> RuinaoTesProtocolBridge -> IHardwareTransport。
/// </summary>
public sealed class MockDeviceClient : IDeviceClient
{
    public Task ConnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task DisconnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task HandshakeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task ReadProductModelAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task ReadBoardModelAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task ReadImpedanceAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task StartTemperatureAcquisitionAsync(ushort periodMs = 200, uint channelMask = 0, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task StopTemperatureAcquisitionAsync(uint channelMask = 0, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task StartImpedanceAcquisitionAsync(ushort periodMs = 200, uint channelMask = 0, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task StopImpedanceAcquisitionAsync(uint channelMask = 0, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task SendParametersAsync(TiGroup group, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task PauseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task EmergencyStopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
