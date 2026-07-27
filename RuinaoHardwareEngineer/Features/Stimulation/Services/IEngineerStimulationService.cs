using RuinaoTesHardware;
using RuinaoTesProtocol.V15;

namespace RuinaoHardwareEngineer.Features.Stimulation.Services;

public interface IEngineerStimulationService
{
    Task<BackplaneStimulationConfigurationResult> ConfigureAsync(
        byte targetAddress,
        TesV15StimulationConfiguration configuration,
        BackplaneConnectionOptions options,
        CancellationToken cancellationToken = default);

    Task<BackplaneRegisterOperationResult> StartAsync(
        byte targetAddress,
        BackplaneConnectionOptions options,
        CancellationToken cancellationToken = default);

    Task<BackplaneRegisterOperationResult> StopAsync(
        byte targetAddress,
        BackplaneConnectionOptions options,
        CancellationToken cancellationToken = default);

    Task<BackplaneStimulationStatusResult> ReadStatusAsync(
        byte targetAddress,
        BackplaneConnectionOptions options,
        CancellationToken cancellationToken = default);
}
