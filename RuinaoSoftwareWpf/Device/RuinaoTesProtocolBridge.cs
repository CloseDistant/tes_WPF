using System.Globalization;
using System.Text.RegularExpressions;
using RuinaoTesHardware;
using RuinaoTesProtocol;

namespace RuinaoSoftwareWpf;

/// <summary>
/// 睿脑 tES 共用硬件 DLL 的 WPF 桥接实现。
///
/// 设计依据：
/// - 《tES 医疗设备控制系统 - 软件开发执行文档》要求 WPF 业务层通过 HAL/协议管理层访问硬件。
/// - 《tES通信协议V1.0》规定上位机地址 0xF0、背板地址 0xF1、业务板地址 0~7，
///   并通过握手、ACK、请求、回复和寄存器读写完成交互。
///
/// 当前职责：
/// - 作为 WPF 调用 RuinaoTesHardware.dll 与 RuinaoTesProtocol.dll 的集中入口。
/// - 上层 HardwareService 只表达“联机、握手、启动刺激、采集阻抗”等业务动作。
/// - 背板/业务板地址、命令码、寄存器地址、byte[] 封包、CRC 等细节由协议 DLL 和本 Bridge 统一处理。
///
/// 注意：
/// 联机和握手已经调用真实RuinaoTesHardware.dll；尚未完成V1.4迁移的其他业务命令暂保留原Transport入口。
/// </summary>
public sealed partial class RuinaoTesProtocolBridge
{
    // 协议 DLL 入口。协议封包、寄存器命令等底层细节集中在 DLL 和 Bridge 内部。
    private readonly TesProtocolApi protocol = new();
    private readonly IHardwareTransport transport;
    private readonly BackplaneClient backplaneClient;
    private readonly ILoggingService logger;
    private static readonly TimeSpan InitialLinkStabilizationDelay = TimeSpan.FromMilliseconds(500);
    private static readonly BackplaneConnectionOptions ProbeHandshakeOptions = new(
        ProtocolVersion: 0x01,
        Timeout: TimeSpan.FromMilliseconds(500),
        HandshakeAckRequired: false);
    private static readonly BackplaneConnectionOptions BackplaneOptions = new(
        ProtocolVersion: 0x01,
        Timeout: TimeSpan.FromSeconds(2),
        HandshakeAckRequired: false);

    // 当前已下发参数的 TI 组。启动/暂停/急停需要知道目标通道。
    private TiGroup? currentGroup;

    /// <summary>
    /// 构造协议桥接层。
    /// BackplaneClient负责真实联机/握手；transport暂承接尚未迁移的业务指令。
    /// </summary>
    public RuinaoTesProtocolBridge(
        IHardwareTransport transport,
        BackplaneClient backplaneClient,
        ILoggingService logger)
    {
        this.transport = transport;
        this.backplaneClient = backplaneClient;
        this.logger = logger;
        backplaneClient.Log += BackplaneClient_Log;
    }

    /// <summary>
    /// 联机：发现04B4:00F1、打开libusbK链路并完成一次真实V1.4背板握手。
    /// </summary>
    public async Task<BackplaneHandshakeResult> ConnectAsync(CancellationToken cancellationToken = default)
    {
        var newlyOpened = await EnsureUsbLinkOpenAsync(cancellationToken);
        if (newlyOpened)
        {
            logger.Hardware("USB接收循环已就绪，等待500ms后发送首次握手帧");
            await Task.Delay(InitialLinkStabilizationDelay, cancellationToken);
        }

        // 第一帧只用于消耗可能被硬件忽略的首次序列号（现场常见为seq=1）。
        // 无论预热帧成功还是超时，均不作为联机依据；下一帧才是正式联机握手。
        try
        {
            var probe = await backplaneClient.HandshakeAsync(ProbeHandshakeOptions, cancellationToken);
            logger.Hardware(
                $"[PROBE_OK] 预热握手已完成但不作为联机依据：seq={probe.RequestSequence} "
                + $"ackSeq={probe.ResponseAckSequence} 耗时={probe.Elapsed.TotalMilliseconds:F1}ms");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.Warning($"[PROBE_IGNORED] 预热握手未成功，按设计忽略并继续正式握手：{exception.Message}");
        }

        logger.Hardware("[CONNECT_HANDSHAKE] 开始发送正式联机握手；只有本帧成功才进入联机状态并启动心跳");
        return await backplaneClient.HandshakeAsync(BackplaneOptions, cancellationToken);
    }

    /// <summary>
    /// 断开真实USB链路并释放libusbK句柄。
    /// </summary>
    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        return backplaneClient.DisconnectAsync(cancellationToken);
    }

    /// <summary>
    /// 发送握手帧。联机和心跳检测共用该动作。
    /// </summary>
    public async Task<BackplaneHandshakeResult> HandshakeAsync(CancellationToken cancellationToken = default)
    {
        var newlyOpened = await EnsureUsbLinkOpenAsync(cancellationToken);
        if (newlyOpened)
        {
            logger.Hardware("USB接收循环已就绪，等待500ms后发送单次握手检测帧");
            await Task.Delay(InitialLinkStabilizationDelay, cancellationToken);
        }

        return await backplaneClient.HandshakeAsync(BackplaneOptions, cancellationToken);
    }

    /// <summary>
    /// 只通过 Windows 设备枚举检查目标背板是否仍然存在、驱动是否可用。
    /// 该方法不会发送协议帧，也不会重新打开 USB 端点，供心跳失败后的二次判定使用。
    /// </summary>
    public async Task<bool> IsBackplaneDeviceReadyAsync(CancellationToken cancellationToken = default)
    {
        var device = await backplaneClient.RefreshDeviceAsync(cancellationToken);
        return device?.DriverReady == true;
    }

    private async Task<bool> EnsureUsbLinkOpenAsync(CancellationToken cancellationToken)
    {
        if (backplaneClient.State is BackplaneConnectionState.Disconnected or BackplaneConnectionState.Faulted)
        {
            await backplaneClient.ConnectAsync(BackplaneOptions, cancellationToken);
            return true;
        }

        return false;
    }

    private void BackplaneClient_Log(object? sender, HardwareLogEntry entry)
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
