namespace RuinaoSoftwareWpf.ApplicationContracts;

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
