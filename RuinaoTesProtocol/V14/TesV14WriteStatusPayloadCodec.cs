using System.Buffers.Binary;

namespace RuinaoTesProtocol.V14;

/// <summary>
/// 解析当前V1.6业务板写寄存器后返回的4字节状态载荷。
/// 该格式来自实机回复；状态码0暂按“已接受写入”处理，非0状态的具体枚举等待固件补充。
/// </summary>
public static class TesV14WriteStatusPayloadCodec
{
    public const int PayloadLength = sizeof(uint);
    public const uint AcceptedStatus = 0;

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
