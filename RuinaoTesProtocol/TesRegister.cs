namespace RuinaoTesProtocol;

/// <summary>
/// Register addresses listed in tES communication protocol V1.0.
/// </summary>
public enum TesRegister : ushort
{
    Reboot = 0x0000,
    PowerOff = 0x0001,
    ProductModel = 0x0002,
    BoardModel = 0x0003,
    Impedance = 0x0010,
    Temperature = 0x0011,
    StimulationParameters = 0x0020,
    AcquisitionParameters = 0x0021,
    OutputStimulationChannel = 0x0022,
}
