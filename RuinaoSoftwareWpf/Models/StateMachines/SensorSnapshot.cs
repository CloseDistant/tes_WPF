namespace RuinaoSoftwareWpf;

public sealed record SensorSnapshot(
    IReadOnlyList<double> TemperaturesC,
    IReadOnlyList<double> ImpedancesOhm,
    DateTimeOffset Timestamp);
