namespace RuinaoTesHardware;

public enum BackplaneConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Handshaking,
    Faulted,
}

public sealed record BackplaneConnectionOptions(
    // 写入V1.4帧头的协议版本字段；usbtest实物代码使用0x01。
    byte ProtocolVersion,
    // 同时用于USB Bulk IN/OUT端点，一次收发超过此时间即判定超时。
    TimeSpan Timeout,
    // false完全复现usbtest握手；true按V1.4文字要求设置“需要ACK”控制位。
    bool HandshakeAckRequired = false);

/// <summary>
/// 一次成功握手的完整结果。保留原始收发帧，便于工程师软件显示十六进制日志和现场追查。
/// </summary>
public sealed record BackplaneHandshakeResult(
    ushort RequestSequence,
    TimeSpan Elapsed,
    byte ResponseVersion,
    byte[] RequestFrame,
    byte[] ResponseFrame,
    byte ResponseCommand = 0,
    ushort ResponseAckSequence = 0);

/// <summary>一次普通寄存器读写操作的真实收发结果。</summary>
public sealed record BackplaneRegisterOperationResult(
    ushort RequestSequence,
    TimeSpan Elapsed,
    byte TargetAddress,
    bool IsWrite,
    IReadOnlyList<RuinaoTesProtocol.V14.TesV14RegisterValue> Registers,
    byte[] RequestFrame,
    byte[] ResponseFrame,
    byte ResponseCommand,
    ushort ResponseAckSequence);

/// <summary>背板产品信息区一个字符串分组的完整读取结果。</summary>
public sealed record BackplaneProductInfoTextResult(
    RuinaoTesProtocol.V14.TesV14ProductInfoGrouping Grouping,
    int GroupIndex,
    ushort StartAddress,
    ushort EndAddress,
    string Text,
    int Utf8ByteCount,
    TimeSpan Elapsed,
    IReadOnlyList<BackplaneRegisterOperationResult> BatchResults);

/// <summary>背板产品信息区一个字符串分组的分批写入结果。</summary>
public sealed record BackplaneProductInfoTextWriteResult(
    RuinaoTesProtocol.V14.TesV14ProductInfoGrouping Grouping,
    int GroupIndex,
    ushort StartAddress,
    ushort EndAddress,
    int Utf8ByteCount,
    TimeSpan Elapsed,
    IReadOnlyList<BackplaneRegisterOperationResult> BatchResults);

public sealed record HardwareLogEntry(
    DateTimeOffset Timestamp,
    string Category,
    string Message,
    byte[]? Bytes = null);

/// <summary>
/// USB写操作完成记录。Frame保存本次实际交给UsbK_WritePipe的完整byte[]副本。
/// </summary>
public sealed record UsbWriteCompletedEventArgs(
    DateTimeOffset Timestamp,
    byte[] Frame,
    int BytesWritten);

/// <summary>
/// 后台接收线程拆出的完整V1.4协议帧。MatchedRequest表示该帧是否已匹配到当前等待中的请求。
/// </summary>
public sealed record UsbFrameReceivedEventArgs(
    DateTimeOffset Timestamp,
    byte[] Frame,
    ushort SendSequence,
    ushort AckSequence,
    bool MatchedRequest,
    bool IntermediateAcknowledgement = false);

/// <summary>libusbK传输层的只读运行快照，供工程师软件诊断USB和协议链路。</summary>
public sealed record UsbTransportDiagnosticSnapshot(
    bool IsOpen,
    bool ReceiveLoopRunning,
    byte BulkOutEndpoint,
    byte BulkInEndpoint,
    long TransmittedFrameCount,
    long ReceivedFrameCount,
    long ReceivedByteCount,
    long MatchedFrameCount,
    long UnmatchedFrameCount,
    long IntermediateAcknowledgementCount,
    long InvalidFrameCount,
    long ExchangeTimeoutCount,
    ushort? PendingSequence,
    int BufferedByteCount,
    DateTimeOffset? LastTransmitTime,
    DateTimeOffset? LastReceiveTime);

/// <summary>
/// 可选的传输诊断接口。业务层通过它区分“已经生成帧”和“USB已经完整写入”。
/// </summary>
public interface IBackplaneTransferDiagnostics
{
    /// <summary>最近一次被USB驱动确认完整写入的帧；尚未成功写入时为null。</summary>
    byte[]? LastWrittenFrame { get; }

    /// <summary>仅在UsbK_WritePipe成功且写入字节数等于帧长度时触发。</summary>
    event EventHandler<UsbWriteCompletedEventArgs>? WriteCompleted;

    /// <summary>Endpoint 0x81收到数据并成功拆出完整协议帧时触发。</summary>
    event EventHandler<UsbFrameReceivedEventArgs>? FrameReceived;

    /// <summary>取得当前USB接收线程、帧计数和待应答请求等只读状态。</summary>
    UsbTransportDiagnosticSnapshot GetSnapshot();
}

public interface IUsbBackplaneDiscovery
{
    Task<UsbBackplaneDevice?> FindAsync(CancellationToken cancellationToken = default);
}

public interface IBackplaneTransport : IAsyncDisposable
{
    /// <summary>是否已经取得可用的USB设备句柄。</summary>
    bool IsOpen { get; }

    /// <summary>打开设备并找到Bulk OUT、Bulk IN端点，此步骤还不代表协议握手成功。</summary>
    Task OpenAsync(UsbBackplaneDevice device, TimeSpan timeout, CancellationToken cancellationToken = default);

    /// <summary>
    /// 向Bulk OUT写入一帧，并等待后台接收循环从Bulk IN拆出与发送序列匹配的回复。
    /// 同一传输实例中的请求会串行执行，避免心跳与业务命令争用硬件。
    /// </summary>
    Task<byte[]> ExchangeAsync(ReadOnlyMemory<byte> request, CancellationToken cancellationToken = default);

    /// <summary>释放USB句柄，断开软件与设备之间的通信链路。</summary>
    Task CloseAsync(CancellationToken cancellationToken = default);
}

public sealed class BackplaneConnectionException : Exception
{
    public BackplaneConnectionException(string message) : base(message)
    {
    }

    public BackplaneConnectionException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
