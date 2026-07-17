namespace RuinaoTesProtocol.V14;

/// <summary>V1.4/usbtest兼容协议入口。</summary>
public sealed class TesV14ProtocolApi
{
    private readonly object sequenceLock = new();
    private ushort nextSequence = 1;

    public byte SourceAddress { get; }
    public byte DestinationAddress { get; set; }
    public byte ProtocolVersion { get; set; }

    public TesV14ProtocolApi(
        byte protocolVersion = TesV14ProtocolConstants.UsbTestProtocolVersion,
        byte sourceAddress = TesV14ProtocolConstants.HostAddress,
        byte destinationAddress = TesV14ProtocolConstants.BackplaneAddress)
    {
        ProtocolVersion = protocolVersion;
        SourceAddress = sourceAddress;
        DestinationAddress = destinationAddress;
    }

    /// <summary>
    /// 生成背板握手帧。usbtest默认不设置ACK控制位，但硬件仍会回包，
    /// 因而兼容模式的ackRequired默认值为false。
    /// </summary>
    public byte[] BuildBackplaneHandshake(out ushort sendSequence, bool ackRequired = false)
    {
        sendSequence = NextSequence();
        return TesV14ProtocolCodec.BuildFrame(
            ackRequired ? TesV14FrameControl.AckRequired : TesV14FrameControl.None,
            TesV14Command.Handshake,
            SourceAddress,
            DestinationAddress,
            sendSequence,
            0,
            ReadOnlySpan<byte>.Empty,
            ProtocolVersion);
    }

    private ushort NextSequence()
    {
        lock (sequenceLock)
        {
            var result = nextSequence++;
            if (nextSequence == 0)
            {
                nextSequence = 1;
            }

            return result;
        }
    }
}
