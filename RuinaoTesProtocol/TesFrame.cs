namespace RuinaoTesProtocol;

/// <summary>
/// Decoded tES protocol frame.
/// Layout: marker + control + version + command + reserved + source + reserved + destination + reserved
/// + send sequence + ack sequence + payload length + payload + CRC16.
/// </summary>
public sealed record TesFrame(
    TesFrameControl Control,
    byte Version,
    TesCommand Command,
    byte SourceAddress,
    byte DestinationAddress,
    ushort SendSequence,
    ushort AckSequence,
    byte[] Payload,
    ushort Crc);
