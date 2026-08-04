using RuinaoTesHardware;

namespace RuinaoSoftwareWpf;

/// <summary>
/// WPF 与共用硬件 DLL 之间的防腐层。
/// 本类只完成应用模型映射和日志转接，不拼帧、不声明寄存器地址，也不直接访问 libusbK。
/// </summary>
public sealed class RuinaoTesHardwareBridge
{
    private readonly TesHardwareDeviceClient hardwareClient;
    private readonly ILoggingService logger;

    public RuinaoTesHardwareBridge(
        TesHardwareDeviceClient hardwareClient,
        ILoggingService logger)
    {
        this.hardwareClient = hardwareClient;
        this.logger = logger;
        hardwareClient.Log += HardwareClient_Log;
    }

    public Task<BackplaneHandshakeResult> ConnectAsync(
        CancellationToken cancellationToken = default) =>
        hardwareClient.ConnectAsync(cancellationToken);

    public Task DisconnectAsync(CancellationToken cancellationToken = default) =>
        hardwareClient.DisconnectAsync(cancellationToken);

    public Task<BackplaneHandshakeResult> HandshakeAsync(
        CancellationToken cancellationToken = default) =>
        hardwareClient.HandshakeAsync(cancellationToken);

    public Task<bool> IsBackplaneDeviceReadyAsync(
        CancellationToken cancellationToken = default) =>
        hardwareClient.IsDeviceReadyAsync(cancellationToken);

    public Task<uint> ReadProductModelAsync(CancellationToken cancellationToken = default) =>
        hardwareClient.ReadProductModelAsync(cancellationToken);

    public Task<uint> ReadBoardModelAsync(CancellationToken cancellationToken = default) =>
        hardwareClient.ReadBoardModelAsync(cancellationToken);

    public Task<uint> ReadImpedanceAsync(CancellationToken cancellationToken = default) =>
        hardwareClient.ReadImpedanceAsync(cancellationToken);

    public Task<TesDeviceTopologySnapshot> ReadDeviceTopologyAsync(
        CancellationToken cancellationToken = default) =>
        hardwareClient.ReadDeviceTopologyAsync(cancellationToken);

    public Task<TesStimulationImpedanceSnapshot> ReadStimulationBoardImpedanceAsync(
        byte boardAddress,
        CancellationToken cancellationToken = default) =>
        hardwareClient.ReadStimulationBoardImpedanceAsync(boardAddress, cancellationToken);

    /// <summary>把产品单位参数交给共用DLL，由DLL完成校验、换算、拼帧和回复判断。</summary>
    internal async Task ConfigureDirectCurrentAsync(
        DirectCurrentHardwareParameters parameters,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        _ = await hardwareClient.ConfigureDirectCurrentAsync(
            new DirectCurrentStimulationParameters(
                parameters.BoardAddress,
                parameters.PhysicalChannelNumber,
                parameters.CurrentMilliampere,
                parameters.RampUpSeconds,
                parameters.RampDownSeconds,
                parameters.TotalDurationSeconds,
                parameters.IsContinuous
                    ? DirectCurrentDeliveryMode.Continuous
                    : DirectCurrentDeliveryMode.Intermittent,
                parameters.IntervalSeconds,
                parameters.SingleDurationSeconds,
                parameters.ReversePolarity
                    ? DirectCurrentPolarity.Reversed
                    : DirectCurrentPolarity.Normal),
            cancellationToken);
    }

    internal async Task StartDirectCurrentChannelsAsync(
        byte boardAddress,
        uint channelMask,
        CancellationToken cancellationToken = default)
    {
        _ = await hardwareClient.StartDirectCurrentChannelsAsync(
            boardAddress,
            channelMask,
            cancellationToken);
    }

    internal async Task StopDirectCurrentChannelsAsync(
        byte boardAddress,
        uint channelMask,
        CancellationToken cancellationToken = default)
    {
        _ = await hardwareClient.StopDirectCurrentChannelsAsync(
            boardAddress,
            channelMask,
            cancellationToken);
    }

    /// <summary>只发送背板0x0003=0；不遍历业务板，不执行通道拉低。</summary>
    internal async Task EmergencyStopBackplaneAsync(
        CancellationToken cancellationToken = default)
    {
        await hardwareClient.EmergencyStopBackplaneAsync(cancellationToken);
    }

    /// <summary>
    /// 暂存上位机业务参数日志。生产刺激 API 尚未从临时分支迁入共用硬件 DLL，
    /// 因此这里不得生成旧协议帧或把日志传输冒充为硬件确认。
    /// </summary>
    public Task SendTiParametersAsync(
        TiGroup group,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(group);

        foreach (var channel in group.Channels)
        {
            logger.Hardware(
                $"PARAM channel={channel.Name} current={channel.CurrentMA}mA "
                + $"freq={channel.FrequencyHz}Hz duration={channel.DurationS}s "
                + $"anode={channel.Anode} cathode={channel.Cathode}");
        }

        return Task.CompletedTask;
    }

    public Task StartTiAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        throw CreateStimulationApiNotMigratedException();
    }

    public Task StopTiAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        throw CreateStimulationApiNotMigratedException();
    }

    public Task EmergencyStopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        throw CreateStimulationApiNotMigratedException();
    }

    private void HardwareClient_Log(object? sender, HardwareLogEntry entry)
    {
        if (entry.Bytes is { Length: > 0 } bytes)
        {
            if (entry.Category.StartsWith("TX", StringComparison.Ordinal))
            {
                logger.HardwareTx(entry.Category, bytes);
            }
            else
            {
                logger.HardwareRx(entry.Category, bytes);
            }
        }

        logger.Hardware($"[{entry.Category}] {entry.Message}");
    }

    private static NotSupportedException CreateStimulationApiNotMigratedException() =>
        new("生产分支的刺激业务 API 尚未迁移到 RuinaoTesHardware，已禁止继续使用旧版协议拼帧链路。");
}
