using System.Diagnostics;
using RuinaoTesProtocol.V14;

namespace RuinaoTesHardware;

/// <summary>
/// tES背板的业务入口。
/// 上层WPF只调用本类，不直接依赖libusbK，也不自行拼装协议字节。
/// </summary>
public sealed class BackplaneClient : IAsyncDisposable
{
    private readonly IUsbBackplaneDiscovery discovery;
    private readonly IBackplaneTransport transport;
    private readonly TesV14ProtocolApi protocolApi = new();
    private readonly object protocolLock = new();

    public BackplaneConnectionState State { get; private set; } = BackplaneConnectionState.Disconnected;
    public UsbBackplaneDevice? Device { get; private set; }

    public event EventHandler<HardwareLogEntry>? Log;
    public event EventHandler<BackplaneConnectionState>? StateChanged;

    public BackplaneClient(IUsbBackplaneDiscovery discovery, IBackplaneTransport transport)
    {
        this.discovery = discovery;
        this.transport = transport;

        // libusbK传输层在UsbK_WritePipe完整成功后回报TX_OK。
        // 这条日志和组帧时的TX_BUILD含义不同，即使随后等待ACK超时也会保留下来。
        if (transport is IBackplaneTransferDiagnostics diagnostics)
        {
            diagnostics.WriteCompleted += Transport_WriteCompleted;
            diagnostics.FrameReceived += Transport_FrameReceived;
        }
    }

    public async Task<UsbBackplaneDevice?> RefreshDeviceAsync(CancellationToken cancellationToken = default)
    {
        // 这里只查询Windows当前是否枚举到目标VID/PID，同时检查驱动是否就绪；不会打开USB端点。
        Device = await discovery.FindAsync(cancellationToken);
        WriteLog("DEVICE", Device is null
            ? "未发现tES背板（VID_04B4&PID_00F1）。"
            : $"发现{Device.Description}：{Device.InstanceId}；{Device.DriverStatus}。");
        return Device;
    }

    public async Task ConnectAsync(
        BackplaneConnectionOptions options,
        CancellationToken cancellationToken = default)
    {
        if (State == BackplaneConnectionState.Connected)
        {
            return;
        }

        MoveTo(BackplaneConnectionState.Connecting);
        try
        {
            // 第一步：通过Windows设备管理接口找到04B4:00F1，并确认绑定的是libusbK驱动。
            var device = await RefreshDeviceAsync(cancellationToken)
                ?? throw new BackplaneConnectionException("未发现tES背板，请检查USB连接和供电。");
            if (!device.DriverReady)
            {
                throw new BackplaneConnectionException(
                    $"发现背板，但{device.DriverStatus}。请先安装tES libusbK驱动。");
            }

            // 第二步：打开libusbK设备句柄并寻找Bulk OUT/Bulk IN端点。
            // 到这里仅说明USB通道可用，还没有证明硬件能识别tES V1.4协议。
            await transport.OpenAsync(device, options.Timeout, cancellationToken);
            MoveTo(BackplaneConnectionState.Connected);
            WriteLog("LINK", "usbtest兼容链路已打开且后台接收循环已就绪：libusbK/WinUsb API，OUT=0x01，IN=0x81。握手成功后才代表协议联机成功。");
        }
        catch
        {
            MoveTo(BackplaneConnectionState.Faulted);
            throw;
        }
    }

    public async Task<BackplaneHandshakeResult> HandshakeAsync(
        BackplaneConnectionOptions options,
        CancellationToken cancellationToken = default)
    {
        if (!transport.IsOpen)
        {
            throw new BackplaneConnectionException("请先执行联机并成功打开libusbK链路。");
        }

        MoveTo(BackplaneConnectionState.Handshaking);
        try
        {
            // 第一步：按V1.4大端序生成握手帧。默认完全复现usbtest：版本0x01、控制域0x0000。
            byte[] request;
            ushort requestSequence;
            lock (protocolLock)
            {
                // 协议对象在整个客户端生命周期中复用，序列号与usbtest一样持续递增，
                // 避免每次握手都从seq=1开始而被硬件当成重复请求。
                protocolApi.ProtocolVersion = options.ProtocolVersion;
                request = protocolApi.BuildBackplaneHandshake(
                    out requestSequence,
                    options.HandshakeAckRequired);
            }
            WriteLog(
                "TX_BUILD",
                $"V1.4/usbtest握手帧已生成：seq={requestSequence} version=0x{options.ProtocolVersion:X2} "
                    + $"ackFlag={(options.HandshakeAckRequired ? 1 : 0)} bytes={request.Length}",
                request);

            // 第二步：硬件DLL将请求写到Bulk OUT，然后阻塞等待Bulk IN返回一帧。
            var stopwatch = Stopwatch.StartNew();
            var response = await transport.ExchangeAsync(request, cancellationToken);
            stopwatch.Stop();
            WriteLog("RX", $"HANDSHAKE response bytes={response.Length}", response);

            // 第三步：先验证帧头、帧尾、声明长度和CRC，再提取命令、地址、序列号等字段。
            if (!TesV14ProtocolCodec.TryParseFrame(response, out var frame, out var error) || frame is null)
            {
                throw new BackplaneConnectionException($"握手回复帧解析失败：{error}");
            }

            // usbtest只显示回包，未定义严格成功条件。共享DLL接受ACK或从机握手回复，拒绝无关命令。
            if (frame.Command is not (TesV14Command.Acknowledgement or TesV14Command.Handshake))
            {
                throw new BackplaneConnectionException(
                    $"握手期望ACK(0x01)或握手回复(0x00)，实际命令为0x{(byte)frame.Command:X2}。");
            }

            // 兼容usbtest未设置ACK控制位的情况：硬件若填写应答序列则必须匹配；填0时暂时接受并记录。
            if (frame.AckSequence != 0 && frame.AckSequence != requestSequence)
            {
                throw new BackplaneConnectionException(
                    $"ACK序列不匹配：expected={requestSequence}, actual={frame.AckSequence}。");
            }

            // 第六步：回复方向必须是背板F1 -> 主机F0，避免接受来源错误的帧。
            if (frame.SourceAddress != TesV14ProtocolConstants.BackplaneAddress
                || frame.DestinationAddress != TesV14ProtocolConstants.HostAddress)
            {
                throw new BackplaneConnectionException(
                    $"ACK地址错误：source=0x{frame.SourceAddress:X2}, destination=0x{frame.DestinationAddress:X2}。");
            }

            // 上述所有检查均通过，才向上层报告“握手成功”。
            MoveTo(BackplaneConnectionState.Connected);
            WriteLog(
                "DECISION",
                $"背板握手成功：command=0x{(byte)frame.Command:X2} ackSeq={frame.AckSequence} "
                    + $"耗时={stopwatch.Elapsed.TotalMilliseconds:F1}ms version=0x{frame.Version:X2}。");
            return new BackplaneHandshakeResult(
                requestSequence,
                stopwatch.Elapsed,
                frame.Version,
                request,
                response,
                (byte)frame.Command,
                frame.AckSequence);
        }
        catch
        {
            // 握手失败不关闭已经打开的USB句柄，状态退回“USB已联机”，方便工程师直接重试。
            MoveTo(BackplaneConnectionState.Connected);
            throw;
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        await transport.CloseAsync(cancellationToken);
        MoveTo(BackplaneConnectionState.Disconnected);
        WriteLog("LINK", "libusbK链路已关闭。");
    }

    public async ValueTask DisposeAsync()
    {
        if (transport is IBackplaneTransferDiagnostics diagnostics)
        {
            diagnostics.WriteCompleted -= Transport_WriteCompleted;
            diagnostics.FrameReceived -= Transport_FrameReceived;
        }

        await transport.DisposeAsync();
    }

    private void Transport_WriteCompleted(object? sender, UsbWriteCompletedEventArgs entry)
    {
        WriteLog(
            "TX_OK",
            $"USB完整写入成功：bytes={entry.BytesWritten}。",
            entry.Frame);
    }

    private void Transport_FrameReceived(object? sender, UsbFrameReceivedEventArgs entry)
    {
        WriteLog(
            entry.MatchedRequest ? "RX_MATCH" : "RX_LATE",
            entry.MatchedRequest
                ? $"收到并匹配本次回复：sendSeq={entry.SendSequence} ackSeq={entry.AckSequence} bytes={entry.Frame.Length}。"
                : $"收到未匹配帧（可能是迟到回复或主动上报）：sendSeq={entry.SendSequence} ackSeq={entry.AckSequence} bytes={entry.Frame.Length}。",
            entry.Frame);
    }

    private void MoveTo(BackplaneConnectionState state)
    {
        State = state;
        StateChanged?.Invoke(this, state);
    }

    private void WriteLog(string category, string message, byte[]? bytes = null)
    {
        Log?.Invoke(this, new HardwareLogEntry(DateTimeOffset.Now, category, message, bytes));
    }
}
