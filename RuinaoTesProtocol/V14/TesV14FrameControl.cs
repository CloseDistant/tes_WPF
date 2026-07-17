namespace RuinaoTesProtocol.V14;

[Flags]
public enum TesV14FrameControl : ushort
{
    None = 0,
    AckRequired = 1 << 0,
    ExtendedAddress = 1 << 2,
}
