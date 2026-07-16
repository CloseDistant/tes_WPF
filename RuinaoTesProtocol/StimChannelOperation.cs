namespace RuinaoTesProtocol;

/// <summary>
/// One 4-byte operation item for register 0x0022:
/// channel number 1 byte, start/stop 1 byte, reserved 2 bytes.
/// </summary>
public sealed record StimChannelOperation(byte ChannelNumber, bool Start);
