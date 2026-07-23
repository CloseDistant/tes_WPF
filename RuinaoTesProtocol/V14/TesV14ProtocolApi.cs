namespace RuinaoTesProtocol.V14;

/// <summary>V1.4/usbtest兼容协议入口。</summary>
public sealed class TesV14ProtocolApi
{
    private readonly object sequenceLock = new();
    private ushort nextSequence = 1;

    public byte SourceAddress { get; }
    public byte DestinationAddress { get; set; }
    public byte ProtocolVersion { get; set; }

    /// <summary>下一条协议帧将使用的发送序号，仅供诊断界面显示。</summary>
    public ushort NextSequenceForDiagnostics
    {
        get
        {
            lock (sequenceLock)
            {
                return nextSequence;
            }
        }
    }

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

    /// <summary>生成普通寄存器读取帧；读取项的4字节内容按usbtest约定填0。</summary>
    public byte[] BuildReadRegisters(
        IReadOnlyList<ushort> addresses,
        out ushort sendSequence,
        bool ackRequired = false)
    {
        return BuildRegisterFrame(
            TesV14Command.Read,
            TesV14RegisterPayloadCodec.EncodeRead(addresses),
            out sendSequence,
            ackRequired);
    }

    /// <summary>生成普通寄存器写入帧。</summary>
    public byte[] BuildWriteRegisters(
        IReadOnlyList<TesV14RegisterValue> registers,
        out ushort sendSequence,
        bool ackRequired = false)
    {
        return BuildRegisterFrame(
            TesV14Command.Write,
            TesV14RegisterPayloadCodec.Encode(registers),
            out sendSequence,
            ackRequired);
    }

    private byte[] BuildRegisterFrame(
        TesV14Command command,
        ReadOnlySpan<byte> payload,
        out ushort sendSequence,
        bool ackRequired)
    {
        sendSequence = NextSequence();
        return TesV14ProtocolCodec.BuildFrame(
            ackRequired ? TesV14FrameControl.AckRequired : TesV14FrameControl.None,
            command,
            SourceAddress,
            DestinationAddress,
            sendSequence,
            0,
            payload,
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

    /// <summary>
    /// 设置下一条协议帧的发送序号，仅用于工程师软件验证65535到1的循环。
    /// 正式业务代码不应改变正常连续递增的序号。
    /// </summary>
    public void SetNextSequenceForDiagnostics(ushort sequence)
    {
        if (sequence == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence), "V1.4发送序号不能为0。");
        }

        lock (sequenceLock)
        {
            nextSequence = sequence;
        }
    }
}
