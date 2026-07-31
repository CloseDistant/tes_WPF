namespace RuinaoTesHardware;

/// <summary>
/// 产品软件和工程师软件共用的硬件业务入口。
/// 上层只调用业务方法，不接触协议帧、寄存器地址、USB 端点或 libusbK。
/// </summary>
public sealed class TesHardwareDeviceClient
{
    private static readonly TimeSpan InitialLinkStabilizationDelay = TimeSpan.FromMilliseconds(500);
    private static readonly BackplaneConnectionOptions ProbeHandshakeOptions = new(
        ProtocolVersion: 0x01,
        Timeout: TimeSpan.FromMilliseconds(500),
        HandshakeAckRequired: false);
    private static readonly BackplaneConnectionOptions DefaultOptions = new(
        ProtocolVersion: 0x01,
        Timeout: TimeSpan.FromSeconds(2),
        HandshakeAckRequired: false);

    private readonly BackplaneClient backplaneClient;

    public TesHardwareDeviceClient(BackplaneClient backplaneClient)
    {
        this.backplaneClient = backplaneClient;
    }

    public BackplaneConnectionState State => backplaneClient.State;

    public event EventHandler<HardwareLogEntry>? Log
    {
        add => backplaneClient.Log += value;
        remove => backplaneClient.Log -= value;
    }

    /// <summary>
    /// 打开 USB 链路，执行一次不作为联机依据的预热握手，再执行正式握手。
    /// 只有正式握手收到并校验有效回复后才返回成功。
    /// </summary>
    public async Task<BackplaneHandshakeResult> ConnectAsync(
        CancellationToken cancellationToken = default)
    {
        var newlyOpened = await EnsureUsbLinkOpenAsync(cancellationToken);
        if (newlyOpened)
        {
            await Task.Delay(InitialLinkStabilizationDelay, cancellationToken);
        }

        try
        {
            _ = await backplaneClient.HandshakeAsync(ProbeHandshakeOptions, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // 预热帧只用于规避部分固件忽略首次序号的现象，不作为联机成败依据。
        }

        return await backplaneClient.HandshakeAsync(DefaultOptions, cancellationToken);
    }

    /// <summary>发送一次握手；必要时先打开 USB 链路并等待其稳定。</summary>
    public async Task<BackplaneHandshakeResult> HandshakeAsync(
        CancellationToken cancellationToken = default)
    {
        var newlyOpened = await EnsureUsbLinkOpenAsync(cancellationToken);
        if (newlyOpened)
        {
            await Task.Delay(InitialLinkStabilizationDelay, cancellationToken);
        }

        return await backplaneClient.HandshakeAsync(DefaultOptions, cancellationToken);
    }

    public Task DisconnectAsync(CancellationToken cancellationToken = default) =>
        backplaneClient.DisconnectAsync(cancellationToken);

    public async Task<bool> IsDeviceReadyAsync(CancellationToken cancellationToken = default)
    {
        var device = await backplaneClient.RefreshDeviceAsync(cancellationToken);
        return device?.DriverReady == true;
    }

    public Task<uint> ReadProductModelAsync(CancellationToken cancellationToken = default) =>
        backplaneClient.ReadProductModelAsync(DefaultOptions, cancellationToken);

    public Task<uint> ReadBoardModelAsync(CancellationToken cancellationToken = default) =>
        backplaneClient.ReadBoardModelAsync(DefaultOptions, cancellationToken);

    public Task<uint> ReadImpedanceAsync(CancellationToken cancellationToken = default) =>
        backplaneClient.ReadImpedanceAsync(DefaultOptions, cancellationToken);

    private async Task<bool> EnsureUsbLinkOpenAsync(CancellationToken cancellationToken)
    {
        if (backplaneClient.State is BackplaneConnectionState.Disconnected
            or BackplaneConnectionState.Faulted)
        {
            await backplaneClient.ConnectAsync(DefaultOptions, cancellationToken);
            return true;
        }

        return false;
    }
}
