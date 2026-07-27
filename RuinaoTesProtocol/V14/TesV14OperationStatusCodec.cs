using System.Buffers.Binary;

namespace RuinaoTesProtocol.V14;

/// <summary>
/// 解析实物固件对写寄存器命令返回的4字节操作状态。
/// 状态码采用协议统一的大端字节序，0表示本次写操作已被硬件接受。
/// </summary>
public static class TesV14OperationStatusCodec
{
    public const int PayloadLength = sizeof(uint);

    public static bool TryDecode(ReadOnlySpan<byte> payload, out uint statusCode)
    {
        if (payload.Length != PayloadLength)
        {
            statusCode = 0;
            return false;
        }

        statusCode = BinaryPrimitives.ReadUInt32BigEndian(payload);
        return true;
    }
}
