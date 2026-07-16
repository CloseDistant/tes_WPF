namespace RuinaoTesProtocol;

/// <summary>
/// tES V1.0 protocol codec.
/// This layer only packs and unpacks protocol bytes. It does not bind to serial, USB, TCP, or any other transport.
/// </summary>
public static class TesProtocolCodec
{
    public static byte[] BuildFrame(
        TesFrameControl control,
        TesCommand command,
        byte sourceAddress,
        byte destinationAddress,
        ushort sendSequence,
        ushort ackSequence,
        ReadOnlySpan<byte> payload,
        byte version = TesProtocolConstants.ProtocolVersion)
    {
        if (payload.Length > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(payload), "Payload length cannot exceed 65535 bytes.");
        }

        var frameWithoutCrc = new byte[TesProtocolConstants.HeaderLength + payload.Length];
        var offset = 0;

        WriteUInt16LE(frameWithoutCrc, ref offset, TesProtocolConstants.FrameMarker);
        WriteUInt16LE(frameWithoutCrc, ref offset, (ushort)control);
        frameWithoutCrc[offset++] = version;
        frameWithoutCrc[offset++] = (byte)command;
        WriteUInt16LE(frameWithoutCrc, ref offset, 0);
        frameWithoutCrc[offset++] = sourceAddress;
        frameWithoutCrc[offset++] = 0;
        frameWithoutCrc[offset++] = destinationAddress;
        frameWithoutCrc[offset++] = 0;
        WriteUInt16LE(frameWithoutCrc, ref offset, sendSequence);
        WriteUInt16LE(frameWithoutCrc, ref offset, ackSequence);
        WriteUInt16LE(frameWithoutCrc, ref offset, (ushort)payload.Length);
        payload.CopyTo(frameWithoutCrc.AsSpan(offset));

        var crc = Crc16.Compute(frameWithoutCrc);
        var frame = new byte[frameWithoutCrc.Length + TesProtocolConstants.CrcLength];
        frameWithoutCrc.CopyTo(frame, 0);

        // The protocol table marks CRC16 as high bit first, so the two CRC bytes are written in big-endian order.
        frame[^2] = (byte)(crc >> 8);
        frame[^1] = (byte)(crc & 0xFF);
        return frame;
    }

    public static bool TryParseFrame(ReadOnlySpan<byte> bytes, out TesFrame? frame, out string error)
    {
        frame = null;
        error = string.Empty;

        if (bytes.Length < TesProtocolConstants.HeaderLength + TesProtocolConstants.CrcLength)
        {
            error = "Frame is too short.";
            return false;
        }

        var offset = 0;
        var marker = ReadUInt16LE(bytes, ref offset);
        if (marker != TesProtocolConstants.FrameMarker)
        {
            error = $"Invalid frame marker: 0x{marker:X4}.";
            return false;
        }

        var control = (TesFrameControl)ReadUInt16LE(bytes, ref offset);
        var version = bytes[offset++];
        var command = (TesCommand)bytes[offset++];
        _ = ReadUInt16LE(bytes, ref offset);
        var source = bytes[offset++];
        offset++;
        var destination = bytes[offset++];
        offset++;
        var sendSequence = ReadUInt16LE(bytes, ref offset);
        var ackSequence = ReadUInt16LE(bytes, ref offset);
        var length = ReadUInt16LE(bytes, ref offset);

        var expectedLength = TesProtocolConstants.HeaderLength + length + TesProtocolConstants.CrcLength;
        if (bytes.Length != expectedLength)
        {
            error = $"Frame length mismatch. Expected {expectedLength}, actual {bytes.Length}.";
            return false;
        }

        var payload = bytes.Slice(offset, length).ToArray();
        var expectedCrc = (ushort)((bytes[^2] << 8) | bytes[^1]);
        var actualCrc = Crc16.Compute(bytes[..^2]);
        if (actualCrc != expectedCrc)
        {
            error = $"CRC mismatch. Expected 0x{expectedCrc:X4}, actual 0x{actualCrc:X4}.";
            return false;
        }

        frame = new TesFrame(control, version, command, source, destination, sendSequence, ackSequence, payload, expectedCrc);
        return true;
    }

    public static void WriteUInt16LE(Span<byte> buffer, ref int offset, ushort value)
    {
        buffer[offset++] = (byte)(value & 0xFF);
        buffer[offset++] = (byte)(value >> 8);
    }

    public static ushort ReadUInt16LE(ReadOnlySpan<byte> buffer, ref int offset)
    {
        var value = (ushort)(buffer[offset] | (buffer[offset + 1] << 8));
        offset += 2;
        return value;
    }
}
