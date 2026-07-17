namespace RuinaoTesProtocol.V14;

public sealed record TesV14Frame(
    TesV14FrameControl Control,
    byte Version,
    TesV14Command Command,
    byte SourceAddress,
    byte DestinationAddress,
    ushort SendSequence,
    ushort AckSequence,
    byte[] Payload,
    ushort Crc,
    ushort FooterMarker);
