namespace RuinaoTesProtocol;

public enum TesTemperatureSensorRole : byte
{
    Unknown = 0,
    Anode = 1,
    Cathode = 2,
    Board = 3,
}

public sealed record TesTemperatureSample(
    byte ChannelNumber,
    byte ElectrodeNumber,
    TesTemperatureSensorRole SensorRole,
    double Celsius);

public sealed record TesImpedanceSample(
    byte ChannelNumber,
    byte AnodeElectrode,
    byte CathodeElectrode,
    ushort Ohms);

public sealed record TesRealtimeReport(
    TesAcquisitionKind Kind,
    ushort SendSequence,
    DateTimeOffset ReceivedAt,
    IReadOnlyList<TesTemperatureSample> Temperatures,
    IReadOnlyList<TesImpedanceSample> Impedances);
