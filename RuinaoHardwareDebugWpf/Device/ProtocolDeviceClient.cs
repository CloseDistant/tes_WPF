using System.Globalization;
using System.Text.RegularExpressions;
using RuinaoTesProtocol;

namespace RuinaoHardwareDebugWpf;

/// <summary>
/// 旧版：基于 RuinaoTesProtocol 协议库的设备客户端实现。
///
/// 当前主链路已经迁移到 RuinaoTesProtocolBridge，以便在源码中更直观看到 WPF 调 DLL 的集中入口。
/// 本类暂时保留为历史适配层，后续确认无用后可以删除或改造成测试替身。
///
/// 历史阶段：
/// - 负责把联机、启动、暂停、急停等操作翻译成协议帧。
/// - 通过 ILoggingService 记录生成的 TX 帧。
/// - 真实 USB/串口/HID/TCP 传输层尚未接入，所以目前只生成帧，不真正发送给硬件。
/// </summary>
public sealed partial class ProtocolDeviceClient : IDeviceClient
{
    // 协议 API 实例，负责把高层命令转换成字节数组。
    private readonly TesProtocolApi protocol = new();
    private readonly ILoggingService logger;

    // 当前已下发参数的 TI 组。Start/Pause/EmergencyStop 需要知道要操作哪些通道。
    private TiGroup? currentGroup;

    public ProtocolDeviceClient(ILoggingService logger)
    {
        this.logger = logger;
    }

    /// <summary>
    /// 联机：当前实现直接发送握手帧。
    /// </summary>
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        await HandshakeAsync(cancellationToken);
    }

    /// <summary>
    /// 断开：当前只记录日志，真实传输层尚未接入。
    /// </summary>
    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        logger.HardwareDecision("断开请求：当前仅断开软件侧状态，真实 USB/串口/HID/TCP 传输层尚未接入。");
        return Task.CompletedTask;
    }

    /// <summary>
    /// 发送握手帧。
    /// </summary>
    public async Task HandshakeAsync(CancellationToken cancellationToken = default)
    {
        await SendFrameAsync("HANDSHAKE", protocol.BuildHandshake(), cancellationToken);
    }

    /// <summary>
    /// 读取产品型号寄存器。
    /// </summary>
    public async Task ReadProductModelAsync(CancellationToken cancellationToken = default)
    {
        await SendFrameAsync("READ_PRODUCT_MODEL", protocol.BuildReadRegister(TesRegister.ProductModel), cancellationToken);
    }

    /// <summary>
    /// 读取板卡型号寄存器。
    /// </summary>
    public async Task ReadBoardModelAsync(CancellationToken cancellationToken = default)
    {
        await SendFrameAsync("READ_BOARD_MODEL", protocol.BuildReadRegister(TesRegister.BoardModel), cancellationToken);
    }

    /// <summary>
    /// 读取阻抗寄存器。
    /// </summary>
    public async Task ReadImpedanceAsync(CancellationToken cancellationToken = default)
    {
        await SendFrameAsync("READ_IMPEDANCE", protocol.BuildReadRegister(TesRegister.Impedance), cancellationToken);
    }

    /// <summary>
    /// 启动温度采集。
    /// </summary>
    public async Task StartTemperatureAcquisitionAsync(ushort periodMs = 200, uint channelMask = 0, CancellationToken cancellationToken = default)
    {
        await SendFrameAsync("START_TEMPERATURE_ACQUISITION", protocol.BuildStartTemperatureAcquisition(periodMs, channelMask), cancellationToken);
    }

    /// <summary>
    /// 停止温度采集。
    /// </summary>
    public async Task StopTemperatureAcquisitionAsync(uint channelMask = 0, CancellationToken cancellationToken = default)
    {
        await SendFrameAsync("STOP_TEMPERATURE_ACQUISITION", protocol.BuildStopTemperatureAcquisition(channelMask), cancellationToken);
    }

    /// <summary>
    /// 启动阻抗采集。
    /// </summary>
    public async Task StartImpedanceAcquisitionAsync(ushort periodMs = 200, uint channelMask = 0, CancellationToken cancellationToken = default)
    {
        await SendFrameAsync("START_IMPEDANCE_ACQUISITION", protocol.BuildStartImpedanceAcquisition(periodMs, channelMask), cancellationToken);
    }

    /// <summary>
    /// 停止阻抗采集。
    /// </summary>
    public async Task StopImpedanceAcquisitionAsync(uint channelMask = 0, CancellationToken cancellationToken = default)
    {
        await SendFrameAsync("STOP_IMPEDANCE_ACQUISITION", protocol.BuildStopImpedanceAcquisition(channelMask), cancellationToken);
    }

    /// <summary>
    /// 下发 TI 刺激参数。
    /// 当前先把参数记录到日志，并保存 group 供 Start/Pause/EmergencyStop 使用。
    /// 后续协议文档确定寄存器 0x0020 的 payload 后，在这里生成真正的参数下发帧。
    /// </summary>
    public async Task SendParametersAsync(TiGroup group, CancellationToken cancellationToken = default)
    {
        currentGroup = group;

        // 把每个通道的参数记录到日志，方便联调时核对。
        foreach (var channel in group.Channels)
        {
            logger.Hardware(
                FormattableString.Invariant(
                    $"PARAM channel={channel.Name} current={channel.CurrentMA}mA freq={channel.FrequencyHz}Hz duration={channel.DurationS}s anode={channel.Anode} cathode={channel.Cathode}"));
        }

        // 协议文档尚未最终确定参数寄存器 payload，暂不生成分组参数帧。
        await Task.CompletedTask;
    }

    /// <summary>
    /// 启动当前组的刺激输出。
    /// 会把组内每个通道解析成通道号，生成通道输出启动帧。
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        var group = EnsureCurrentGroup();
        var operations = group.Channels
            .Select(channel => new StimChannelOperation(ParseChannelNumber(channel.Name), true));

        await SendFrameAsync("START_CHANNELS", protocol.BuildStimChannelOutput(operations), cancellationToken);
    }

    /// <summary>
    /// 暂停当前组的刺激输出。
    /// </summary>
    public async Task PauseAsync(CancellationToken cancellationToken = default)
    {
        var group = EnsureCurrentGroup();
        var operations = group.Channels
            .Select(channel => new StimChannelOperation(ParseChannelNumber(channel.Name), false));

        await SendFrameAsync("STOP_CHANNELS", protocol.BuildStimChannelOutput(operations), cancellationToken);
    }

    /// <summary>
    /// 紧急停止当前组的刺激输出。
    /// </summary>
    public async Task EmergencyStopAsync(CancellationToken cancellationToken = default)
    {
        var group = EnsureCurrentGroup();
        var operations = group.Channels
            .Select(channel => new StimChannelOperation(ParseChannelNumber(channel.Name), false));

        await SendFrameAsync("EMERGENCY_STOP_CHANNELS", protocol.BuildStimChannelOutput(operations), cancellationToken);
    }

    /// <summary>
    /// 确保已经调用过 SendParametersAsync，否则抛异常。
    /// </summary>
    private TiGroup EnsureCurrentGroup()
    {
        return currentGroup ?? throw new InvalidOperationException("No TI group parameters have been sent.");
    }

    /// <summary>
    /// 从通道名称（如 "CH 13"）中解析出数字通道号。
    /// </summary>
    private static byte ParseChannelNumber(string channelName)
    {
        var match = ChannelNumberRegex().Match(channelName);
        if (!match.Success)
        {
            throw new FormatException($"Cannot parse channel number from '{channelName}'.");
        }

        var value = byte.Parse(match.Value, CultureInfo.InvariantCulture);
        if (value == 0)
        {
            throw new FormatException("Channel number must be greater than 0.");
        }

        return value;
    }

    /// <summary>
    /// 发送协议帧的通用方法。
    /// 当前只把帧内容写入日志；真实硬件对接时，在这里调用传输层发送。
    /// </summary>
    private Task SendFrameAsync(string name, byte[] frame, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        logger.HardwareTx(name, frame);
        logger.HardwareDecision("当前尚未接入真实 USB/串口/HID/TCP 传输层，因此本条 TX 仅代表协议帧已生成，尚不代表硬件已收到。");
        return Task.CompletedTask;
    }

    // 编译时生成的正则：匹配一个或多个数字，用于 ParseChannelNumber。
    [GeneratedRegex(@"\d+")]
    private static partial Regex ChannelNumberRegex();
}
