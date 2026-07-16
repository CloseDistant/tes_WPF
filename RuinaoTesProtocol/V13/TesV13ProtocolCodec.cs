namespace RuinaoTesProtocol.V13;

/// <summary>
/// V1.3帧编解码器。CRC暂按现有协议DLL使用的CRC-16/MODBUS实现，
/// 待硬件方提供测试向量后可以只替换CRC实现。
/// </summary>
public static class TesV13ProtocolCodec
{
    public static byte[] BuildFrame(
        TesV13FrameControl control,
        TesV13Command command,
        byte sourceAddress,
        byte destinationAddress,
        ushort sendSequence,
        ushort ackSequence,
        ReadOnlySpan<byte> payload,
        byte version = TesV13ProtocolConstants.DefaultProtocolVersion)
    {
        if (payload.Length > TesV13ProtocolConstants.MaximumPayloadLength)
        {
            throw new ArgumentOutOfRangeException(nameof(payload),
                $"V1.3 payload cannot exceed {TesV13ProtocolConstants.MaximumPayloadLength} bytes.");
        }

        // CRC计算范围是“18字节帧头 + 数据体”，不包含CRC自身和帧尾。
        var crcInput = new byte[TesV13ProtocolConstants.HeaderLength + payload.Length];
        var offset = 0;
        // 以下字段顺序与V1.3文档完全一致；除CRC外，多字节字段均低字节在前。
        WriteUInt16LittleEndian(crcInput, ref offset, TesV13ProtocolConstants.FrameMarker);
        WriteUInt16LittleEndian(crcInput, ref offset, (ushort)control);
        crcInput[offset++] = version;
        crcInput[offset++] = (byte)command;
        WriteUInt16LittleEndian(crcInput, ref offset, 0);
        crcInput[offset++] = sourceAddress;
        crcInput[offset++] = 0;
        crcInput[offset++] = destinationAddress;
        crcInput[offset++] = 0;
        WriteUInt16LittleEndian(crcInput, ref offset, sendSequence);
        WriteUInt16LittleEndian(crcInput, ref offset, ackSequence);
        WriteUInt16LittleEndian(crcInput, ref offset, (ushort)payload.Length);
        payload.CopyTo(crcInput.AsSpan(offset));

        // 完成帧头和数据体后计算CRC，再追加CRC与帧尾，形成最终发送字节。
        var crc = Crc16.Compute(crcInput);
        var frame = new byte[crcInput.Length + TesV13ProtocolConstants.CrcLength + TesV13ProtocolConstants.FooterLength];
        crcInput.CopyTo(frame, 0);

        // V1.3表格规定CRC高字节在前，帧尾标识低字节在前。
        frame[^4] = (byte)(crc >> 8);
        frame[^3] = (byte)(crc & 0xFF);
        frame[^2] = (byte)(TesV13ProtocolConstants.FooterMarker & 0xFF);
        frame[^1] = (byte)(TesV13ProtocolConstants.FooterMarker >> 8);
        return frame;
    }

    public static bool TryParseFrame(ReadOnlySpan<byte> bytes, out TesV13Frame? frame, out string error)
    {
        frame = null;
        error = string.Empty;

        // 解析采用逐层校验：最小长度 -> 帧尾 -> 帧头 -> 总长度 -> CRC。
        // 任意一步失败都不把数据交给业务层，避免误判握手成功。
        if (bytes.Length < TesV13ProtocolConstants.MinimumFrameLength)
        {
            error = $"Frame is too short: {bytes.Length}.";
            return false;
        }

        var footer = (ushort)(bytes[^2] | (bytes[^1] << 8));
        if (footer != TesV13ProtocolConstants.FooterMarker)
        {
            error = $"Invalid footer marker: 0x{footer:X4}.";
            return false;
        }

        var offset = 0;
        var marker = ReadUInt16LittleEndian(bytes, ref offset);
        if (marker != TesV13ProtocolConstants.FrameMarker)
        {
            error = $"Invalid frame marker: 0x{marker:X4}.";
            return false;
        }

        var control = (TesV13FrameControl)ReadUInt16LittleEndian(bytes, ref offset);
        var version = bytes[offset++];
        var command = (TesV13Command)bytes[offset++];
        _ = ReadUInt16LittleEndian(bytes, ref offset);
        var sourceAddress = bytes[offset++];
        offset++;
        var destinationAddress = bytes[offset++];
        offset++;
        var sendSequence = ReadUInt16LittleEndian(bytes, ref offset);
        var ackSequence = ReadUInt16LittleEndian(bytes, ref offset);
        var payloadLength = ReadUInt16LittleEndian(bytes, ref offset);

        var expectedLength = TesV13ProtocolConstants.HeaderLength
            + payloadLength
            + TesV13ProtocolConstants.CrcLength
            + TesV13ProtocolConstants.FooterLength;
        if (bytes.Length != expectedLength)
        {
            error = $"Frame length mismatch. Expected {expectedLength}, actual {bytes.Length}.";
            return false;
        }

        // 文档规定CRC高字节在前，因此这里不能使用普通的小端读取函数。
        var expectedCrc = (ushort)((bytes[^4] << 8) | bytes[^3]);
        var actualCrc = Crc16.Compute(bytes[..^(TesV13ProtocolConstants.CrcLength + TesV13ProtocolConstants.FooterLength)]);
        if (expectedCrc != actualCrc)
        {
            error = $"CRC mismatch. Expected 0x{expectedCrc:X4}, actual 0x{actualCrc:X4}.";
            return false;
        }

        var payload = bytes.Slice(offset, payloadLength).ToArray();
        frame = new TesV13Frame(
            control,
            version,
            command,
            sourceAddress,
            destinationAddress,
            sendSequence,
            ackSequence,
            payload,
            expectedCrc,
            footer);
        return true;
    }

    public static int GetExpectedFrameLength(ReadOnlySpan<byte> header)
    {
        if (header.Length < TesV13ProtocolConstants.HeaderLength)
        {
            throw new ArgumentException("A complete 18-byte header is required.", nameof(header));
        }

        // Bulk IN是字节流。传输层先收满18字节帧头，再用偏移16~17的数据体长度决定还要读取多少字节。
        var payloadLength = header[16] | (header[17] << 8);
        return TesV13ProtocolConstants.HeaderLength
            + payloadLength
            + TesV13ProtocolConstants.CrcLength
            + TesV13ProtocolConstants.FooterLength;
    }

    private static void WriteUInt16LittleEndian(Span<byte> buffer, ref int offset, ushort value)
    {
        buffer[offset++] = (byte)(value & 0xFF);
        buffer[offset++] = (byte)(value >> 8);
    }

    private static ushort ReadUInt16LittleEndian(ReadOnlySpan<byte> buffer, ref int offset)
    {
        var value = (ushort)(buffer[offset] | (buffer[offset + 1] << 8));
        offset += 2;
        return value;
    }
}
