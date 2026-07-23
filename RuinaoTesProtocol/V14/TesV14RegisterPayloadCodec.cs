using System.Buffers.Binary;

namespace RuinaoTesProtocol.V14;

/// <summary>V1.4普通寄存器载荷编解码器，与usbtest的6字节寄存器项格式一致。</summary>
public static class TesV14RegisterPayloadCodec
{
    public const int CountLength = 2;
    public const int EntryLength = 6;

    public static byte[] EncodeRead(IReadOnlyList<ushort> addresses)
    {
        ArgumentNullException.ThrowIfNull(addresses);
        return Encode(addresses.Select(address => new TesV14RegisterValue(address, 0)).ToArray());
    }

    public static byte[] Encode(IReadOnlyList<TesV14RegisterValue> registers)
    {
        ArgumentNullException.ThrowIfNull(registers);
        if (registers.Count is 0 or > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(registers), "寄存器数量必须在1到65535之间。");
        }

        var payloadLength = checked(CountLength + registers.Count * EntryLength);
        if (payloadLength > TesV14ProtocolConstants.MaximumPayloadLength)
        {
            throw new ArgumentOutOfRangeException(nameof(registers), "寄存器数据超过V1.4单帧载荷上限。");
        }

        var payload = new byte[payloadLength];
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(0, 2), (ushort)registers.Count);
        for (var index = 0; index < registers.Count; index++)
        {
            var offset = CountLength + index * EntryLength;
            BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(offset, 2), registers[index].Address);
            BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(offset + 2, 4), registers[index].Value);
        }

        return payload;
    }

    public static bool TryDecode(
        ReadOnlySpan<byte> payload,
        out IReadOnlyList<TesV14RegisterValue> registers,
        out string error)
    {
        registers = Array.Empty<TesV14RegisterValue>();
        error = string.Empty;
        if (payload.Length < CountLength)
        {
            error = "寄存器回复缺少2字节寄存器数量。";
            return false;
        }

        var count = BinaryPrimitives.ReadUInt16BigEndian(payload[..2]);
        if (count == 0)
        {
            error = "寄存器回复中的寄存器数量为0。";
            return false;
        }

        var expectedLength = CountLength + count * EntryLength;
        if (payload.Length != expectedLength)
        {
            error = $"寄存器载荷长度不匹配：count={count}，expected={expectedLength}，actual={payload.Length}。";
            return false;
        }

        var result = new TesV14RegisterValue[count];
        for (var index = 0; index < count; index++)
        {
            var offset = CountLength + index * EntryLength;
            result[index] = new TesV14RegisterValue(
                BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(offset, 2)),
                BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(offset + 2, 4)));
        }

        registers = result;
        return true;
    }
}
