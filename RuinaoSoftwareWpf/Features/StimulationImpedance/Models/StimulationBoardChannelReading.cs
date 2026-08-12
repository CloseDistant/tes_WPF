namespace RuinaoSoftwareWpf;

internal sealed record StimulationBoardChannelReading(
    int PhysicalChannelNumber,
    ushort RegisterAddress,
    uint RawValue,
    decimal ImpedanceOhms);
