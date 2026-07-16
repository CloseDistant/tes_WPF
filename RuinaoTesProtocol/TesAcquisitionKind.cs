namespace RuinaoTesProtocol;

/// <summary>
/// Acquisition item selected by the upper software.
/// The protocol V1.0 document only lists impedance and temperature registers,
/// so these values intentionally mirror the register addresses.
/// </summary>
public enum TesAcquisitionKind : ushort
{
    Impedance = TesRegister.Impedance,
    Temperature = TesRegister.Temperature,
}
