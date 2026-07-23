using System.Buffers.Binary;
using System.Text;

namespace RuinaoTesProtocol.V14;

/// <summary>背板产品信息区支持的固定分组布局。</summary>
public enum TesV14ProductInfoGrouping
{
    Groups16 = 16,
    Groups32 = 32,
}

/// <summary>一种分组布局的只读参数。</summary>
public sealed record TesV14ProductInfoLayout(
    TesV14ProductInfoGrouping Grouping,
    int GroupCount,
    int RegistersPerGroup,
    int CapacityBytes,
    int MaximumTextBytes);

/// <summary>
/// 背板0x0100～0x04FF字符串区编解码，支持16组和32组。
/// 每组内容使用严格UTF-8，首个0字节作为结束标记，未使用空间写0。
/// </summary>
public static class TesV14ProductInfoTextCodec
{
    public const ushort StartAddress = 0x0100;
    public const ushort EndAddress = 0x04FF;
    public const int TotalRegisterCount = EndAddress - StartAddress + 1;
    // 读取是幂等操作，每批8个可以把32组布局从8次往返降到4次，单帧仍只有约72字节。
    public const int ReadBatchSize = 8;

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static TesV14ProductInfoLayout GetLayout(TesV14ProductInfoGrouping grouping)
    {
        var groupCount = grouping switch
        {
            TesV14ProductInfoGrouping.Groups16 => 16,
            TesV14ProductInfoGrouping.Groups32 => 32,
            _ => throw new ArgumentOutOfRangeException(nameof(grouping), "仅支持16组或32组布局。"),
        };
        var registersPerGroup = TotalRegisterCount / groupCount;
        var capacityBytes = registersPerGroup * sizeof(uint);
        return new TesV14ProductInfoLayout(
            grouping,
            groupCount,
            registersPerGroup,
            capacityBytes,
            capacityBytes - 1);
    }

    public static ushort GetGroupStartAddress(TesV14ProductInfoGrouping grouping, int groupIndex)
    {
        var layout = GetLayout(grouping);
        ValidateGroupIndex(layout, groupIndex);
        return (ushort)(StartAddress + groupIndex * layout.RegistersPerGroup);
    }

    public static ushort GetGroupEndAddress(TesV14ProductInfoGrouping grouping, int groupIndex) =>
        (ushort)(GetGroupStartAddress(grouping, groupIndex) + GetLayout(grouping).RegistersPerGroup - 1);

    public static IReadOnlyList<ushort> GetAddresses(TesV14ProductInfoGrouping grouping, int groupIndex)
    {
        var layout = GetLayout(grouping);
        var startAddress = GetGroupStartAddress(grouping, groupIndex);
        return Enumerable.Range(startAddress, layout.RegistersPerGroup).Select(value => (ushort)value).ToArray();
    }

    public static IReadOnlyList<TesV14RegisterValue> Encode(
        TesV14ProductInfoGrouping grouping,
        int groupIndex,
        string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Contains('\0'))
        {
            throw new ArgumentException("字符串不能包含空字符，因为0字节用于标识文本结束。", nameof(text));
        }

        var layout = GetLayout(grouping);
        var textBytes = StrictUtf8.GetBytes(text);
        if (textBytes.Length > layout.MaximumTextBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(text),
                $"{layout.GroupCount}组布局每组最多{layout.MaximumTextBytes}个UTF-8字节，实际为{textBytes.Length}字节。");
        }

        var startAddress = GetGroupStartAddress(grouping, groupIndex);
        var area = new byte[layout.CapacityBytes];
        textBytes.CopyTo(area, 0);
        var registers = new TesV14RegisterValue[layout.RegistersPerGroup];
        for (var index = 0; index < registers.Length; index++)
        {
            registers[index] = new TesV14RegisterValue(
                (ushort)(startAddress + index),
                BinaryPrimitives.ReadUInt32BigEndian(area.AsSpan(index * sizeof(uint), sizeof(uint))));
        }

        return registers;
    }

    public static string Decode(
        TesV14ProductInfoGrouping grouping,
        int groupIndex,
        IReadOnlyList<TesV14RegisterValue> registers)
    {
        var area = CombineRegisterBytes(grouping, groupIndex, registers);
        var terminator = Array.IndexOf(area, (byte)0);
        var length = terminator >= 0 ? terminator : area.Length;
        try
        {
            return StrictUtf8.GetString(area, 0, length);
        }
        catch (DecoderFallbackException exception)
        {
            var startAddress = GetGroupStartAddress(grouping, groupIndex);
            var registerAddress = (ushort)(startAddress + Math.Max(0, exception.Index) / sizeof(uint));
            throw new FormatException(
                $"当前字符串组不是有效的UTF-8内容：byteOffset={exception.Index}，register=0x{registerAddress:X4}。",
                exception);
        }
    }

    /// <summary>按地址顺序把当前组所有4字节寄存器内容组合成原始字节。</summary>
    public static byte[] CombineRegisterBytes(
        TesV14ProductInfoGrouping grouping,
        int groupIndex,
        IReadOnlyList<TesV14RegisterValue> registers)
    {
        ArgumentNullException.ThrowIfNull(registers);
        var layout = GetLayout(grouping);
        if (registers.Count != layout.RegistersPerGroup)
        {
            throw new FormatException(
                $"{layout.GroupCount}组布局应返回{layout.RegistersPerGroup}个寄存器，实际为{registers.Count}个。");
        }

        var startAddress = GetGroupStartAddress(grouping, groupIndex);
        var area = new byte[layout.CapacityBytes];
        for (var index = 0; index < registers.Count; index++)
        {
            var expectedAddress = (ushort)(startAddress + index);
            if (registers[index].Address != expectedAddress)
            {
                throw new FormatException(
                    $"字符串组地址不连续：expected=0x{expectedAddress:X4}, actual=0x{registers[index].Address:X4}。");
            }

            BinaryPrimitives.WriteUInt32BigEndian(
                area.AsSpan(index * sizeof(uint), sizeof(uint)),
                registers[index].Value);
        }

        return area;
    }

    public static int GetUtf8ByteCount(string text) => StrictUtf8.GetByteCount(text ?? string.Empty);

    /// <summary>字符串内容连同0结束符实际占用的4字节寄存器数量。</summary>
    public static int GetRequiredRegisterCount(string text)
    {
        var bytesIncludingTerminator = checked(GetUtf8ByteCount(text) + 1);
        return Math.Max(1, (bytesIncludingTerminator + sizeof(uint) - 1) / sizeof(uint));
    }

    private static void ValidateGroupIndex(TesV14ProductInfoLayout layout, int groupIndex)
    {
        if (groupIndex < 0 || groupIndex >= layout.GroupCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(groupIndex),
                $"{layout.GroupCount}组布局的组号必须在0到{layout.GroupCount - 1}之间。");
        }
    }
}
