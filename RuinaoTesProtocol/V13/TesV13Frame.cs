namespace RuinaoTesProtocol.V13;

public sealed record TesV13Frame(
    TesV13FrameControl Control,
    byte Version,
    TesV13Command Command,
    byte SourceAddress,
    byte DestinationAddress,
    ushort SendSequence,
    ushort AckSequence,
    byte[] Payload,
    ushort Crc,
    ushort FooterMarker);
