namespace RuinaoTesProtocol.V13;

/// <summary>
/// tES通信协议V1.3中已经明确的固定字段。
/// </summary>
public static class TesV13ProtocolConstants
{
    public const ushort FrameMarker = 0xA55A;
    public const ushort FooterMarker = 0xB55B;

    // V1.3文档尚未明确版本字段编码。工程师工具默认使用0x13，并允许现场修改。
    public const byte DefaultProtocolVersion = 0x13;

    public const byte HostAddress = 0xF0;
    public const byte BackplaneAddress = 0xF1;

    public const int HeaderLength = 18;
    public const int CrcLength = 2;
    public const int FooterLength = 2;
    public const int MinimumFrameLength = HeaderLength + CrcLength + FooterLength;
    public const int MaximumPayloadLength = ushort.MaxValue - CrcLength - HeaderLength;
}
