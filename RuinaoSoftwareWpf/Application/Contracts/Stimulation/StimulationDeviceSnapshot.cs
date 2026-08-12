namespace RuinaoSoftwareWpf.ApplicationContracts;

public sealed record StimulationDeviceSnapshot(
    StimulationDeviceConnectionState ConnectionState, DateTimeOffset ObservedAt, string? Detail = null);
