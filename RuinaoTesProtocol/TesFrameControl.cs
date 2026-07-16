namespace RuinaoTesProtocol;

/// <summary>
/// Protocol frame control field.
/// bit0: 1 means ACK is required.
/// bit1: 0 means read, 1 means write.
/// Other bits are currently reserved and kept as 0.
/// </summary>
[Flags]
public enum TesFrameControl : ushort
{
    None = 0,
    AckRequired = 1 << 0,
    Write = 1 << 1,
}
