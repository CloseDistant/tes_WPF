using System.Diagnostics;
using RuinaoTesProtocol.V13;

namespace RuinaoTesHardware;

/// <summary>
/// tES背板的业务入口。
/// 上层WPF只调用本类，不直接依赖libusbK，也不自行拼装协议字节。
/// </summary>
public sealed class BackplaneClient : IAsyncDisposable
{
    private readonly IUsbBackplaneDiscovery discovery;
    private readonly IBackplaneTransport transport;

    public BackplaneConnectionState State { get; private set; } = BackplaneConnectionState.Disconnected;
    public UsbBackplaneDevice? Device { get; private set; }

    public event EventHandler<HardwareLogEntry>? Log;
    public event EventHandler<BackplaneConnectionState>? StateChanged;

    public BackplaneClient(IUsbBackplaneDiscovery discovery, IBackplaneTransport transport)
    {
        this.discovery = discovery;
        this.transport = transport;
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
            // 到这里仅说明USB通道可用，还没有证明硬件能识别tES V1.3协议。
            await transport.OpenAsync(device, options.Timeout, cancellationToken);
            MoveTo(BackplaneConnectionState.Connected);
            WriteLog("LINK", "libusbK链路已打开。此状态只表示USB端点可访问，握手成功后才代表协议联机成功。");
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
            // 第一步：协议DLL按V1.3生成握手请求帧。
            // 握手命令为0x00、数据体长度为0，并设置“需要ACK”控制位。
            var api = new TesV13ProtocolApi(options.ProtocolVersion);
            var request = api.BuildHandshake(out var requestSequence);
            WriteLog("TX", $"HANDSHAKE seq={requestSequence} version=0x{options.ProtocolVersion:X2}", request);

            // 第二步：硬件DLL将请求写到Bulk OUT，然后阻塞等待Bulk IN返回一帧。
            var stopwatch = Stopwatch.StartNew();
            var response = await transport.ExchangeAsync(request, cancellationToken);
            stopwatch.Stop();
            WriteLog("RX", $"HANDSHAKE response bytes={response.Length}", response);

            // 第三步：先验证帧头、帧尾、声明长度和CRC，再提取命令、地址、序列号等字段。
            if (!TesV13ProtocolCodec.TryParseFrame(response, out var frame, out var error) || frame is null)
            {
                throw new BackplaneConnectionException($"握手回复帧解析失败：{error}");
            }

            // 第四步：握手必须收到ACK命令0x01，收到其他合法命令也不能算握手成功。
            if (frame.Command != TesV13Command.Acknowledgement)
            {
                throw new BackplaneConnectionException($"握手期望ACK(0x01)，实际命令为0x{(byte)frame.Command:X2}。");
            }

            // 第五步：ACK中的确认序列号必须等于刚才发出的握手序列号，防止把旧回复当成当前回复。
            if (frame.AckSequence != requestSequence)
            {
                throw new BackplaneConnectionException(
                    $"ACK序列不匹配：expected={requestSequence}, actual={frame.AckSequence}。");
            }

            // 第六步：回复方向必须是背板F1 -> 主机F0，避免接受来源错误的帧。
            if (frame.SourceAddress != TesV13ProtocolConstants.BackplaneAddress
                || frame.DestinationAddress != TesV13ProtocolConstants.HostAddress)
            {
                throw new BackplaneConnectionException(
                    $"ACK地址错误：source=0x{frame.SourceAddress:X2}, destination=0x{frame.DestinationAddress:X2}。");
            }

            // 上述所有检查均通过，才向上层报告“握手成功”。
            MoveTo(BackplaneConnectionState.Connected);
            WriteLog("DECISION", $"握手成功，耗时{stopwatch.Elapsed.TotalMilliseconds:F1}ms，硬件版本字段0x{frame.Version:X2}。");
            return new BackplaneHandshakeResult(
                requestSequence,
                stopwatch.Elapsed,
                frame.Version,
                request,
                response);
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
        await transport.DisposeAsync();
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
