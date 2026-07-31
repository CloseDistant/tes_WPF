namespace RuinaoSoftwareWpf.ApplicationContracts;

public enum StimulationDeviceConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Faulted
}

public enum StimulationCommandStatus
{
    Accepted,
    Confirmed,
    Rejected,
    TimedOut,
    Cancelled,
    Disconnected
}

public enum StimulationDeliveryMode
{
    Continuous,
    Intermittent
}

public sealed record StimulationChannelParameters(
    int ChannelNumber,
    int AnodeElectrodeNumber,
    int CathodeElectrodeNumber,
    decimal CurrentMilliampere,
    decimal FrequencyHz,
    int RampUpSeconds,
    int RampDownSeconds,
    int DurationSeconds,
    int? IntervalSeconds = null);

public sealed record StimulationProgram(
    string ProgramId,
    string DisplayName,
    string StimulationType,
    StimulationDeliveryMode DeliveryMode,
    IReadOnlyList<StimulationChannelParameters> Channels);

public sealed record StimulationDeviceSnapshot(
    StimulationDeviceConnectionState ConnectionState,
    DateTimeOffset ObservedAt,
    string? Detail = null);

public sealed record StimulationCommandResult(
    StimulationCommandStatus Status,
    StimulationDeviceSnapshot Device,
    string? ErrorCode = null,
    string? Message = null)
{
    public bool Succeeded =>
        Status is StimulationCommandStatus.Accepted or StimulationCommandStatus.Confirmed;
}

public interface IStimulationDeviceGateway
{
    StimulationDeviceSnapshot Current { get; }

    Task<StimulationCommandResult> ConnectAsync(CancellationToken cancellationToken = default);

    Task<StimulationCommandResult> DisconnectAsync(CancellationToken cancellationToken = default);

    Task<StimulationCommandResult> CheckImpedanceAsync(CancellationToken cancellationToken = default);

    Task<StimulationCommandResult> ConfigureAsync(
        StimulationProgram program,
        CancellationToken cancellationToken = default);

    Task<StimulationCommandResult> StartAsync(CancellationToken cancellationToken = default);

    Task<StimulationCommandResult> StopAsync(CancellationToken cancellationToken = default);

    Task<StimulationCommandResult> EmergencyStopAsync(
        string reason,
        CancellationToken cancellationToken = default);
}
