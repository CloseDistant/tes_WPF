using System.Globalization;
using System.Text.RegularExpressions;
using RuinaoTesProtocol;

namespace RuinaoHardwareDebugWpf;

/// <summary>
/// 瑞脑 tES 协议 DLL 的 WPF 桥接实现。
///
/// 设计依据：
/// - 《tES 医疗设备控制系统 - 软件开发执行文档》要求 WPF 业务层通过 HAL/协议管理层访问硬件。
/// - 《tES通信协议V1.0》规定上位机地址 0xF0、背板地址 0xF1、业务板地址 0~7，
///   并通过握手、ACK、请求、回复和寄存器读写完成交互。
///
/// 当前职责：
/// - 作为 WPF 调用 RuinaoTesProtocol.dll 的集中入口。
/// - 上层 HardwareService 只表达“联机、握手、启动刺激、采集阻抗”等业务动作。
/// - 背板/业务板地址、命令码、寄存器地址、byte[] 封包、CRC 等细节由协议 DLL 和本 Bridge 统一处理。
///
/// 注意：
/// 当前 DLL 仍以 BuildXXX 方法生成协议帧，Bridge 会把生成的帧交给 IHardwareTransport。
/// 后续如果硬件 DLL 直接提供 Connect/Start/Stop/Send 等真实发送方法，应优先在 Bridge 或 Transport 中替换。
/// </summary>
public sealed partial class RuinaoTesProtocolBridge
{
    // 协议 DLL 入口。协议封包、寄存器命令等底层细节集中在 DLL 和 Bridge 内部。
    private readonly TesProtocolApi protocol = new();
    private readonly IHardwareTransport transport;
    private readonly ILoggingService logger;

    // 当前已下发参数的 TI 组。启动/暂停/急停需要知道目标通道。
    private TiGroup? currentGroup;

    /// <summary>
    /// 构造协议桥接层。
    /// transport 决定协议帧最终如何发出去；当前默认是只写日志的 LogOnlyHardwareTransport。
    /// </summary>
    public RuinaoTesProtocolBridge(IHardwareTransport transport, ILoggingService logger)
    {
        this.transport = transport;
        this.logger = logger;
    }

    /// <summary>
    /// 联机：当前协议 DLL 暂以握手帧表示联机请求。
    /// </summary>
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        UseBackplane();
        await HandshakeAsync(cancellationToken);
    }

    /// <summary>
    /// 断开：真实传输层未接入前，只记录软件侧断开意图。
    /// </summary>
    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        logger.HardwareDecision("断开请求：当前仅断开软件侧状态，真实 USB/串口/HID/TCP 传输层尚未接入。");
        return Task.CompletedTask;
    }

    /// <summary>
    /// 发送握手帧。联机和心跳检测共用该动作。
    /// </summary>
    public async Task HandshakeAsync(CancellationToken cancellationToken = default)
    {
        await transport.SendFrameAsync("HANDSHAKE", protocol.BuildHandshake(), cancellationToken);
    }

    /// <summary>
    /// 读取产品型号寄存器。
    /// </summary>
    public async Task ReadProductModelAsync(CancellationToken cancellationToken = default)
    {
        await transport.SendFrameAsync("READ_PRODUCT_MODEL", protocol.BuildReadRegister(TesRegister.ProductModel), cancellationToken);
    }

    /// <summary>
    /// 读取板卡型号寄存器。
    /// </summary>
    public async Task ReadBoardModelAsync(CancellationToken cancellationToken = default)
    {
        await transport.SendFrameAsync("READ_BOARD_MODEL", protocol.BuildReadRegister(TesRegister.BoardModel), cancellationToken);
    }

    /// <summary>
    /// 读取阻抗寄存器。
    /// </summary>
    public async Task ReadImpedanceAsync(CancellationToken cancellationToken = default)
    {
        await transport.SendFrameAsync("READ_IMPEDANCE", protocol.BuildReadRegister(TesRegister.Impedance), cancellationToken);
    }

    /// <summary>
    /// 启动温度采集。默认周期 200ms。
    /// </summary>
    public async Task StartTemperatureAcquisitionAsync(ushort periodMs = 200, uint channelMask = 0, CancellationToken cancellationToken = default)
    {
        await transport.SendFrameAsync("START_TEMPERATURE_ACQUISITION", protocol.BuildStartTemperatureAcquisition(periodMs, channelMask), cancellationToken);
    }

    /// <summary>
    /// 停止温度采集。
    /// </summary>
    public async Task StopTemperatureAcquisitionAsync(uint channelMask = 0, CancellationToken cancellationToken = default)
    {
        await transport.SendFrameAsync("STOP_TEMPERATURE_ACQUISITION", protocol.BuildStopTemperatureAcquisition(channelMask), cancellationToken);
    }

    /// <summary>
    /// 启动阻抗采集。默认周期 200ms。
    /// </summary>
    public async Task StartImpedanceAcquisitionAsync(ushort periodMs = 200, uint channelMask = 0, CancellationToken cancellationToken = default)
    {
        await transport.SendFrameAsync("START_IMPEDANCE_ACQUISITION", protocol.BuildStartImpedanceAcquisition(periodMs, channelMask), cancellationToken);
    }

    /// <summary>
    /// 停止阻抗采集。
    /// </summary>
    public async Task StopImpedanceAcquisitionAsync(uint channelMask = 0, CancellationToken cancellationToken = default)
    {
        await transport.SendFrameAsync("STOP_IMPEDANCE_ACQUISITION", protocol.BuildStopImpedanceAcquisition(channelMask), cancellationToken);
    }

    /// <summary>
    /// 下发 TI 刺激组参数。
    /// 当前协议文档还没确定完整参数 payload，所以先记录业务参数，后续由 DLL 接管封包。
    /// </summary>
    public async Task SendTiParametersAsync(TiGroup group, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        currentGroup = group;

        foreach (var channel in group.Channels)
        {
            logger.Hardware(
                FormattableString.Invariant(
                    $"PARAM channel={channel.Name} current={channel.CurrentMA}mA freq={channel.FrequencyHz}Hz duration={channel.DurationS}s anode={channel.Anode} cathode={channel.Cathode}"));
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// 启动当前 TI 刺激组输出。
    /// </summary>
    public async Task StartTiAsync(CancellationToken cancellationToken = default)
    {
        var group = EnsureCurrentGroup();
        var operations = group.Channels
            .Select(channel => new StimChannelOperation(ParseChannelNumber(channel.Name), true));

        await transport.SendFrameAsync("START_CHANNELS", protocol.BuildStimChannelOutput(operations), cancellationToken);
    }

    /// <summary>
    /// 暂停当前 TI 刺激组输出。
    /// </summary>
    public async Task PauseTiAsync(CancellationToken cancellationToken = default)
    {
        var group = EnsureCurrentGroup();
        var operations = group.Channels
            .Select(channel => new StimChannelOperation(ParseChannelNumber(channel.Name), false));

        await transport.SendFrameAsync("STOP_CHANNELS", protocol.BuildStimChannelOutput(operations), cancellationToken);
    }

    /// <summary>
    /// 紧急停止当前 TI 刺激组输出。
    /// </summary>
    public async Task EmergencyStopAsync(CancellationToken cancellationToken = default)
    {
        var group = EnsureCurrentGroup();
        var operations = group.Channels
            .Select(channel => new StimChannelOperation(ParseChannelNumber(channel.Name), false));

        await transport.SendFrameAsync("EMERGENCY_STOP_CHANNELS", protocol.BuildStimChannelOutput(operations), cancellationToken);
    }

    /// <summary>
    /// 选择背板作为目的地址。
    /// 协议文档规定：上位机默认 0xF0，背板默认 0xF1。
    /// 当前联机、读取背板信息等动作默认发给背板。
    /// </summary>
    public void UseBackplane()
    {
        protocol.DestinationAddress = TesProtocolConstants.BackplaneAddress;
    }

    /// <summary>
    /// 选择某个业务板作为目的地址。
    /// 协议文档规定业务板按槽位编号 0~7；如果目的地址不是背板，背板只做透传。
    /// </summary>
    public void UseBusinessBoard(byte slotAddress)
    {
        if (slotAddress > 7)
        {
            throw new ArgumentOutOfRangeException(nameof(slotAddress), "业务板槽位地址必须在 0~7 之间。");
        }

        protocol.DestinationAddress = slotAddress;
    }

    /// <summary>
    /// 确保已经下发过 TI 参数，避免不知道要操作哪些通道。
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

    // 编译时生成的正则：匹配一个或多个数字，用于 ParseChannelNumber。
    [GeneratedRegex(@"\d+")]
    private static partial Regex ChannelNumberRegex();
}
