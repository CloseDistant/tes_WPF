using System.Buffers.Binary;

namespace RuinaoTesProtocol.V14;

/// <summary>
/// V1.4帧编解码器。V1.4将除单字节字段外的协议字段统一为高字节在前，
/// 与usbtest的WriteUInt16BigEndian实现保持一致。CRC暂按usbtest使用CRC-16/MODBUS。
/// </summary>
public static class TesV14ProtocolCodec
{
    public static byte[] BuildFrame(
        TesV14FrameControl control,
        TesV14Command command,
        byte sourceAddress,
        byte destinationAddress,
        ushort sendSequence,
        ushort ackSequence,
        ReadOnlySpan<byte> payload,
        byte version = TesV14ProtocolConstants.UsbTestProtocolVersion)
    {
        if (payload.Length > TesV14ProtocolConstants.MaximumPayloadLength)
        {
            throw new ArgumentOutOfRangeException(nameof(payload),
                $"V1.4数据体不能超过{TesV14ProtocolConstants.MaximumPayloadLength}字节。");
        }

        var crcInput = new byte[TesV14ProtocolConstants.HeaderLength + payload.Length];
        BinaryPrimitives.WriteUInt16BigEndian(crcInput.AsSpan(0, 2), TesV14ProtocolConstants.FrameMarker);
        BinaryPrimitives.WriteUInt16BigEndian(crcInput.AsSpan(2, 2), (ushort)control);
        crcInput[4] = version;
        crcInput[5] = (byte)command;
        BinaryPrimitives.WriteUInt16BigEndian(crcInput.AsSpan(6, 2), 0);
        crcInput[8] = sourceAddress;
        crcInput[9] = 0;
        crcInput[10] = destinationAddress;
        crcInput[11] = 0;
        BinaryPrimitives.WriteUInt16BigEndian(crcInput.AsSpan(12, 2), sendSequence);
        BinaryPrimitives.WriteUInt16BigEndian(crcInput.AsSpan(14, 2), ackSequence);
        BinaryPrimitives.WriteUInt16BigEndian(crcInput.AsSpan(16, 2), (ushort)payload.Length);
        payload.CopyTo(crcInput.AsSpan(TesV14ProtocolConstants.HeaderLength));

        var crc = Crc16.Compute(crcInput);
        var frame = new byte[crcInput.Length + TesV14ProtocolConstants.CrcLength + TesV14ProtocolConstants.FooterLength];
        crcInput.CopyTo(frame, 0);
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(crcInput.Length, 2), crc);
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(crcInput.Length + 2, 2), TesV14ProtocolConstants.FooterMarker);
        return frame;
    }

    public static bool TryParseFrame(ReadOnlySpan<byte> bytes, out TesV14Frame? frame, out string error)
    {
        frame = null;
        error = string.Empty;
        if (bytes.Length < TesV14ProtocolConstants.MinimumFrameLength)
        {
            error = $"帧长度不足：至少{TesV14ProtocolConstants.MinimumFrameLength}字节，实际{bytes.Length}字节。";
            return false;
        }

        var marker = BinaryPrimitives.ReadUInt16BigEndian(bytes[..2]);
        if (marker != TesV14ProtocolConstants.FrameMarker)
        {
            error = $"帧头错误：0x{marker:X4}。";
            return false;
        }

        var payloadLength = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(16, 2));
        var expectedLength = TesV14ProtocolConstants.HeaderLength + payloadLength
            + TesV14ProtocolConstants.CrcLength + TesV14ProtocolConstants.FooterLength;
        if (bytes.Length != expectedLength)
        {
            error = $"帧长度不匹配：应为{expectedLength}字节，实际{bytes.Length}字节。";
            return false;
        }

        var footer = BinaryPrimitives.ReadUInt16BigEndian(bytes[^2..]);
        if (footer != TesV14ProtocolConstants.FooterMarker)
        {
            error = $"帧尾错误：0x{footer:X4}。";
            return false;
        }

        var receivedCrc = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(bytes.Length - 4, 2));
        var calculatedCrc = Crc16.Compute(bytes[..^4]);
        if (receivedCrc != calculatedCrc)
        {
            error = $"CRC错误：接收0x{receivedCrc:X4}，计算0x{calculatedCrc:X4}。";
            return false;
        }

        frame = new TesV14Frame(
            (TesV14FrameControl)BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(2, 2)),
            bytes[4],
            (TesV14Command)bytes[5],
            bytes[8],
            bytes[10],
            BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(12, 2)),
            BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(14, 2)),
            bytes.Slice(TesV14ProtocolConstants.HeaderLength, payloadLength).ToArray(),
            receivedCrc,
            footer);
        return true;
    }

    public static int GetExpectedFrameLength(ReadOnlySpan<byte> header)
    {
        if (header.Length < TesV14ProtocolConstants.HeaderLength)
        {
            throw new ArgumentException("必须提供完整的18字节V1.4帧头。", nameof(header));
        }

        var payloadLength = BinaryPrimitives.ReadUInt16BigEndian(header.Slice(16, 2));
        return TesV14ProtocolConstants.HeaderLength + payloadLength
            + TesV14ProtocolConstants.CrcLength + TesV14ProtocolConstants.FooterLength;
    }
}
