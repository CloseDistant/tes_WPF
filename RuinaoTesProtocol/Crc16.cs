namespace RuinaoTesProtocol;

/// <summary>
/// CRC16计算工具。
/// 注意：当前协议文档只写了“CRC16”，没有明确多项式、初始值和最终异或值。
/// 当前暂按CRC-16/IBM（Modbus）实现：多项式0xA001、初始值0xFFFF。
/// 正式联调前必须让硬件厂商提供一组完整报文及其CRC结果，用来确认双方算法完全一致。
/// </summary>
public static class Crc16
{
    public static ushort Compute(ReadOnlySpan<byte> data)
    {
        ushort crc = 0xFFFF;
        foreach (var b in data)
        {
            // 每个字节先与CRC低8位异或，再逐位右移计算。
            crc ^= b;
            for (var i = 0; i < 8; i++)
            {
                var lsb = (crc & 0x0001) != 0;
                crc >>= 1;
                if (lsb)
                {
                    crc ^= 0xA001;
                }
            }
        }

        return crc;
    }
}
