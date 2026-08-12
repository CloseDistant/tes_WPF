namespace RuinaoSoftwareWpf;

public sealed record ImpedanceMeasurement(byte Channel, double ImpedanceOhm, DateTimeOffset Timestamp);
