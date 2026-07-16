using System.Buffers.Binary;

namespace RuinaoTesProtocol.V13;

/// <summary>
/// tES 通信协议 V1.3 寄存器数据体编解码。
/// 所有多字节寄存器字段均按低字节在前（Little Endian）处理。
/// </summary>
public static class TesV13RegisterPayloadCodec
{
    public const int RegisterCountLength = 2;
    public const int RegisterAddressLength = 2;
    public const int RegisterContentLength = 4;
    public const int RegisterEntryLength = RegisterAddressLength + RegisterContentLength;
    public const int LargeRegisterContentLength = 1024;

    public const ushort ProductInformationAddress = 0x0100;
    public const ushort BoardInformationAddress = 0x0500;

    public static byte[] Encode(IReadOnlyList<TesV13RegisterValue> registers)
    {
        ArgumentNullException.ThrowIfNull(registers);
        ValidateCount(registers.Count);

        var payloadLength = checked(RegisterCountLength + registers.Count * RegisterEntryLength);
        if (payloadLength > TesV13ProtocolConstants.MaximumPayloadLength)
        {
            throw new ArgumentOutOfRangeException(nameof(registers),
                $"寄存器数据体不能超过 {TesV13ProtocolConstants.MaximumPayloadLength} 字节。");
        }

        var payload = new byte[payloadLength];
        BinaryPrimitives.WriteUInt16LittleEndian(payload, (ushort)registers.Count);
        var offset = RegisterCountLength;
        foreach (var register in registers)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(offset), register.Address);
            offset += RegisterAddressLength;
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(offset), register.Value);
            offset += RegisterContentLength;
        }

        return payload;
    }

    public static byte[] EncodeRead(IReadOnlyList<ushort> addresses)
    {
        ArgumentNullException.ThrowIfNull(addresses);
        var registers = new TesV13RegisterValue[addresses.Count];
        for (var index = 0; index < addresses.Count; index++)
        {
            // V1.3 图示中的读命令仍保留 4 字节寄存器内容，读请求置零。
            registers[index] = new TesV13RegisterValue(addresses[index], 0);
        }

        return Encode(registers);
    }

    public static bool TryDecode(
        ReadOnlySpan<byte> payload,
        out IReadOnlyList<TesV13RegisterValue> registers,
        out string error)
    {
        registers = Array.Empty<TesV13RegisterValue>();
        error = string.Empty;
        if (payload.Length < RegisterCountLength)
        {
            error = "寄存器数据体不足 2 字节，无法读取寄存器个数。";
            return false;
        }

        var count = BinaryPrimitives.ReadUInt16LittleEndian(payload);
        if (count == 0)
        {
            error = "寄存器个数不能为 0。";
            return false;
        }

        var expectedLength = RegisterCountLength + count * RegisterEntryLength;
        if (payload.Length != expectedLength)
        {
            error = $"寄存器数据体长度不匹配：个数={count}，应为 {expectedLength} 字节，实际 {payload.Length} 字节。";
            return false;
        }

        var result = new TesV13RegisterValue[count];
        var offset = RegisterCountLength;
        for (var index = 0; index < count; index++)
        {
            var address = BinaryPrimitives.ReadUInt16LittleEndian(payload[offset..]);
            offset += RegisterAddressLength;
            var value = BinaryPrimitives.ReadUInt32LittleEndian(payload[offset..]);
            offset += RegisterContentLength;
            result[index] = new TesV13RegisterValue(address, value);
        }

        registers = result;
        return true;
    }

    public static byte[] EncodeLarge(ushort address, ReadOnlySpan<byte> content)
    {
        if (!IsLargeRegister(address))
        {
            throw new ArgumentOutOfRangeException(nameof(address),
                "1KB 特殊寄存器地址只能是产品信息 0x0100 或单板信息 0x0500。");
        }

        if (content.Length != LargeRegisterContentLength)
        {
            throw new ArgumentException($"特殊寄存器内容必须恰好为 {LargeRegisterContentLength} 字节。", nameof(content));
        }

        var payload = new byte[RegisterCountLength + RegisterAddressLength + LargeRegisterContentLength];
        BinaryPrimitives.WriteUInt16LittleEndian(payload, 1);
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(RegisterCountLength), address);
        content.CopyTo(payload.AsSpan(RegisterCountLength + RegisterAddressLength));
        return payload;
    }

    public static bool TryDecodeLarge(
        ReadOnlySpan<byte> payload,
        out ushort address,
        out byte[] content,
        out string error)
    {
        address = 0;
        content = Array.Empty<byte>();
        error = string.Empty;
        var expectedLength = RegisterCountLength + RegisterAddressLength + LargeRegisterContentLength;
        if (payload.Length != expectedLength)
        {
            error = $"特殊寄存器数据体应为 {expectedLength} 字节，实际 {payload.Length} 字节。";
            return false;
        }

        var count = BinaryPrimitives.ReadUInt16LittleEndian(payload);
        if (count != 1)
        {
            error = $"特殊寄存器个数必须为 1，实际为 {count}。";
            return false;
        }

        address = BinaryPrimitives.ReadUInt16LittleEndian(payload[RegisterCountLength..]);
        if (!IsLargeRegister(address))
        {
            error = $"0x{address:X4} 不是 V1.3 定义的 1KB 特殊寄存器。";
            address = 0;
            return false;
        }

        content = payload[(RegisterCountLength + RegisterAddressLength)..].ToArray();
        return true;
    }

    public static bool IsLargeRegister(ushort address) =>
        address is ProductInformationAddress or BoardInformationAddress;

    private static void ValidateCount(int count)
    {
        if (count is <= 0 or > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "寄存器个数必须在 1 到 65535 之间。 ");
        }
    }
}
