namespace RuinaoTesProtocol.V13;

[Flags]
public enum TesV13FrameControl : ushort
{
    None = 0,
    AckRequired = 1 << 0,
    ExtendedAddress = 1 << 2,
}
