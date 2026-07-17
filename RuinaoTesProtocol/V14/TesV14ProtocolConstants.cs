namespace RuinaoTesProtocol.V14;

/// <summary>V1.4固定字段及usbtest实物联调默认值。</summary>
public static class TesV14ProtocolConstants
{
    public const ushort FrameMarker = 0xA55A;
    public const ushort FooterMarker = 0xB55B;

    // V1.4文档未给出版本字段的具体编码；usbtest实物代码固定使用0x01。
    public const byte UsbTestProtocolVersion = 0x01;
    public const byte HostAddress = 0xF0;
    public const byte BackplaneAddress = 0xF1;

    public const int HeaderLength = 18;
    public const int CrcLength = 2;
    public const int FooterLength = 2;
    public const int MinimumFrameLength = HeaderLength + CrcLength + FooterLength;
    public const int MaximumPayloadLength = 65515;
}
