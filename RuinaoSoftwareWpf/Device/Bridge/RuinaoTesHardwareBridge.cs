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

    internal async Task ConfigureMonophasicPulseCurrentAsync(
        MonophasicPulseCurrentHardwareParameters parameters,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        _ = await hardwareClient.ConfigureMonophasicPulseCurrentAsync(
            new MonophasicPulseCurrentStimulationParameters(
                parameters.BoardAddress,
                parameters.PhysicalChannelNumber,
                parameters.CurrentMilliampere,
                parameters.RampUpDownSeconds,
                parameters.IntervalSeconds,
                parameters.TotalDurationSeconds),
            cancellationToken);
    }

    internal async Task StartMonophasicPulseCurrentChannelsAsync(
        byte boardAddress,
        uint channelMask,
        CancellationToken cancellationToken = default)
    {
        _ = await hardwareClient.StartMonophasicPulseCurrentChannelsAsync(
            boardAddress,
            channelMask,
            cancellationToken);
    }

    internal async Task StopMonophasicPulseCurrentChannelsAsync(
        byte boardAddress,
        uint channelMask,
        CancellationToken cancellationToken = default)
    {
        _ = await hardwareClient.StopMonophasicPulseCurrentChannelsAsync(
            boardAddress,
            channelMask,
            cancellationToken);
    }

    internal async Task EmergencyStopMonophasicPulseCurrentBackplaneAsync(
        CancellationToken cancellationToken = default)
    {
        _ = await hardwareClient.EmergencyStopMonophasicPulseCurrentBackplaneAsync(
            cancellationToken);
    }

    internal async Task ConfigurePulseCurrentAsync(
        PulseCurrentHardwareParameters parameters,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        _ = await hardwareClient.ConfigurePulseCurrentAsync(
            new PulseCurrentStimulationParameters(
                parameters.BoardAddress,
                parameters.PhysicalChannelNumber,
                parameters.CurrentMilliampere,
                parameters.RampWidthMilliseconds,
                parameters.PulseWidthMilliseconds,
                parameters.IntervalWidthMilliseconds,
                parameters.TreatmentDurationSeconds,
                parameters.ReversePolarity
                    ? PulseCurrentPolarity.Reversed
                    : PulseCurrentPolarity.Normal),
            cancellationToken);
    }

    internal async Task StartPulseCurrentChannelsAsync(
        byte boardAddress,
        uint channelMask,
        CancellationToken cancellationToken = default)
    {
        _ = await hardwareClient.StartPulseCurrentChannelsAsync(
            boardAddress,
            channelMask,
            cancellationToken);
    }

    internal async Task StopPulseCurrentChannelsAsync(
        byte boardAddress,
        uint channelMask,
        CancellationToken cancellationToken = default)
    {
        _ = await hardwareClient.StopPulseCurrentChannelsAsync(
            boardAddress,
            channelMask,
            cancellationToken);
    }

    internal async Task EmergencyStopPulseCurrentBackplaneAsync(
        CancellationToken cancellationToken = default)
    {
        _ = await hardwareClient.EmergencyStopPulseCurrentBackplaneAsync(cancellationToken);
    }

    /// <summary>只发送背板0x0003=0；不遍历业务板，不执行通道拉低。</summary>
    internal async Task EmergencyStopBackplaneAsync(
        CancellationToken cancellationToken = default)
    {
        _ = await hardwareClient.EmergencyStopBackplaneAsync(cancellationToken);
    }

    internal int GetAlternatingCurrentConfigurationCommandCount(
        AlternatingCurrentHardwareParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        return AlternatingCurrentStimulationClient.CreatePlan(ToAlternatingCurrentParameters(parameters))
            .Segments.Count + 1;
    }

    internal async Task ConfigureAlternatingCurrentAsync(
        AlternatingCurrentHardwareParameters parameters,
        IProgress<AlternatingCurrentHardwareConfigurationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        var progressAdapter = progress is null
            ? null
            : new AlternatingCurrentProgressAdapter(progress);
        _ = await hardwareClient.ConfigureAlternatingCurrentAsync(
            ToAlternatingCurrentParameters(parameters),
            progressAdapter,
            cancellationToken);
    }

    internal async Task StartAlternatingCurrentChannelsAsync(
        byte boardAddress,
        uint channelMask,
        CancellationToken cancellationToken = default)
    {
        _ = await hardwareClient.StartAlternatingCurrentChannelsAsync(
            boardAddress,
            channelMask,
            cancellationToken);
    }

    internal async Task StopAlternatingCurrentChannelsAsync(
        byte boardAddress,
        uint channelMask,
        CancellationToken cancellationToken = default)
    {
        _ = await hardwareClient.StopAlternatingCurrentChannelsAsync(
            boardAddress,
            channelMask,
            cancellationToken);
    }

    private static AlternatingCurrentStimulationParameters ToAlternatingCurrentParameters(
        AlternatingCurrentHardwareParameters parameters) =>
        new(
            parameters.BoardAddress,
            parameters.PhysicalChannelNumber,
            parameters.PeakCurrentMilliampere,
            parameters.RampUpSeconds,
            parameters.RampDownSeconds,
            parameters.FrequencyHz,
            parameters.TotalDurationSeconds);

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

    private sealed class AlternatingCurrentProgressAdapter(
        IProgress<AlternatingCurrentHardwareConfigurationProgress> target)
        : IProgress<AlternatingCurrentConfigurationProgress>
    {
        public void Report(AlternatingCurrentConfigurationProgress value)
        {
            target.Report(new AlternatingCurrentHardwareConfigurationProgress(
                value.CompletedCommandCount,
                value.TotalCommandCount,
                value.Stage));
        }
    }
}
