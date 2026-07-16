namespace RuinaoTesProtocol;

/// <summary>
/// tES 通信协议 V1.0 中已明确的固定值。
/// 未在 PRD 中明确的细节，例如 CRC16 多项式、具体寄存器 payload 格式，
/// 在本类库中保留为可配置/可扩展点。
/// </summary>
public static class TesProtocolConstants
{
    public const ushort FrameMarker = 0xA55A;
    public const byte ProtocolVersion = 0x01;

    public const byte HostAddress = 0xF0;
    public const byte BackplaneAddress = 0xF1;

    public const int HeaderLength = 18;
    public const int CrcLength = 2;
}
