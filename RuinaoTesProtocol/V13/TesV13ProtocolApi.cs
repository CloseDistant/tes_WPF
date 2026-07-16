namespace RuinaoTesProtocol.V13;

/// <summary>
/// 面向上层业务的V1.3组帧接口。
/// 本类只负责协议字节，不访问USB；工程师软件和正式WPF软件应共同引用这一份实现。
/// </summary>
public sealed class TesV13ProtocolApi
{
    private readonly object sequenceLock = new();
    private ushort nextSequence = 1;

    public byte SourceAddress { get; }
    public byte DestinationAddress { get; set; }
    public byte ProtocolVersion { get; set; }

    public TesV13ProtocolApi(
        byte protocolVersion = TesV13ProtocolConstants.DefaultProtocolVersion,
        byte sourceAddress = TesV13ProtocolConstants.HostAddress,
        byte destinationAddress = TesV13ProtocolConstants.BackplaneAddress)
    {
        ProtocolVersion = protocolVersion;
        SourceAddress = sourceAddress;
        DestinationAddress = destinationAddress;
    }

    public byte[] BuildHandshake(out ushort sendSequence, bool ackRequired = true)
    {
        // 每次主动发送都分配一个非0序列号，硬件回复ACK时应把它放进AckSequence字段。
        sendSequence = NextSequence();
        var control = ackRequired ? TesV13FrameControl.AckRequired : TesV13FrameControl.None;

        // 握手命令没有数据体，所以payload为空；ackSequence为0表示这不是对其他帧的回复。
        return TesV13ProtocolCodec.BuildFrame(
            control,
            TesV13Command.Handshake,
            SourceAddress,
            DestinationAddress,
            sendSequence,
            0,
            ReadOnlySpan<byte>.Empty,
            ProtocolVersion);
    }

    public byte[] BuildReadRegisters(
        IReadOnlyList<ushort> addresses,
        out ushort sendSequence,
        bool ackRequired = true)
    {
        return BuildRegisterFrame(
            TesV13Command.Read,
            TesV13RegisterPayloadCodec.EncodeRead(addresses),
            out sendSequence,
            ackRequired);
    }

    public byte[] BuildWriteRegisters(
        IReadOnlyList<TesV13RegisterValue> registers,
        out ushort sendSequence,
        bool ackRequired = true)
    {
        return BuildRegisterFrame(
            TesV13Command.Write,
            TesV13RegisterPayloadCodec.Encode(registers),
            out sendSequence,
            ackRequired);
    }

    public byte[] BuildResponse(
        IReadOnlyList<TesV13RegisterValue> registers,
        ushort ackSequence,
        out ushort sendSequence)
    {
        sendSequence = NextSequence();
        return TesV13ProtocolCodec.BuildFrame(
            TesV13FrameControl.None,
            TesV13Command.Response,
            SourceAddress,
            DestinationAddress,
            sendSequence,
            ackSequence,
            TesV13RegisterPayloadCodec.Encode(registers),
            ProtocolVersion);
    }

    public byte[] BuildWriteLargeRegister(
        ushort address,
        ReadOnlySpan<byte> content,
        out ushort sendSequence,
        bool ackRequired = true)
    {
        return BuildRegisterFrame(
            TesV13Command.Write,
            TesV13RegisterPayloadCodec.EncodeLarge(address, content),
            out sendSequence,
            ackRequired);
    }

    public byte[] BuildReadLargeRegister(
        ushort address,
        out ushort sendSequence,
        bool ackRequired = true)
    {
        // V1.3 将 0x0100/0x0500 定义为固定 1KB 内容；读请求按图示用零填充内容区。
        return BuildRegisterFrame(
            TesV13Command.Read,
            TesV13RegisterPayloadCodec.EncodeLarge(
                address,
                new byte[TesV13RegisterPayloadCodec.LargeRegisterContentLength]),
            out sendSequence,
            ackRequired);
    }

    public static bool TryParseRegisters(
        TesV13Frame frame,
        out IReadOnlyList<TesV13RegisterValue> registers,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (frame.Command is not (TesV13Command.Read or TesV13Command.Write or TesV13Command.Response))
        {
            registers = Array.Empty<TesV13RegisterValue>();
            error = $"命令 0x{(byte)frame.Command:X2} 不包含普通寄存器数据体。";
            return false;
        }

        return TesV13RegisterPayloadCodec.TryDecode(frame.Payload, out registers, out error);
    }

    public static bool TryParseLargeRegister(
        TesV13Frame frame,
        out ushort address,
        out byte[] content,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (frame.Command is not (TesV13Command.Read or TesV13Command.Write or TesV13Command.Response))
        {
            address = 0;
            content = Array.Empty<byte>();
            error = $"命令 0x{(byte)frame.Command:X2} 不包含特殊寄存器数据体。";
            return false;
        }

        return TesV13RegisterPayloadCodec.TryDecodeLarge(frame.Payload, out address, out content, out error);
    }

    private byte[] BuildRegisterFrame(
        TesV13Command command,
        ReadOnlySpan<byte> payload,
        out ushort sendSequence,
        bool ackRequired)
    {
        sendSequence = NextSequence();
        var control = ackRequired ? TesV13FrameControl.AckRequired : TesV13FrameControl.None;
        return TesV13ProtocolCodec.BuildFrame(
            control,
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
            // ushort溢出后跳过0，从1重新开始；加锁保证多个异步操作不会拿到同一个序列号。
            var value = nextSequence++;
            if (nextSequence == 0)
            {
                nextSequence = 1;
            }

            return value;
        }
    }
}
