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
    // 写入V1.3帧头的协议版本字段，当前界面默认使用0x13。
    byte ProtocolVersion,
    // 同时用于USB Bulk IN/OUT端点，一次收发超过此时间即判定超时。
    TimeSpan Timeout);

/// <summary>
/// 一次成功握手的完整结果。保留原始收发帧，便于工程师软件显示十六进制日志和现场追查。
/// </summary>
public sealed record BackplaneHandshakeResult(
    ushort RequestSequence,
    TimeSpan Elapsed,
    byte ResponseVersion,
    byte[] RequestFrame,
    byte[] ResponseFrame);

public sealed record HardwareLogEntry(
    DateTimeOffset Timestamp,
    string Category,
    string Message,
    byte[]? Bytes = null);

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

    /// <summary>向Bulk OUT写入一帧，再从Bulk IN读取一帧完整回复。</summary>
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
