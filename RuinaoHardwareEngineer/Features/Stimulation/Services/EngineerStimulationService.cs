using RuinaoTesHardware;
using RuinaoTesProtocol.V15;

namespace RuinaoHardwareEngineer.Features.Stimulation.Services;

public sealed class EngineerStimulationService(BackplaneClient client) : IEngineerStimulationService
{
    public Task<BackplaneStimulationConfigurationResult> ConfigureAsync(
        byte targetAddress,
        TesV15StimulationConfiguration configuration,
        BackplaneConnectionOptions options,
        CancellationToken cancellationToken = default)
    {
        return client.ConfigureStimulationAsync(targetAddress, configuration, options, cancellationToken);
    }

    public Task<BackplaneUsbSendResult> StartAsync(
        byte targetAddress,
        BackplaneConnectionOptions options,
        CancellationToken cancellationToken = default)
    {
        return client.StartStimulationAsync(targetAddress, options, cancellationToken);
    }

    public Task<BackplaneUsbSendResult> StopAsync(
        byte targetAddress,
        BackplaneConnectionOptions options,
        CancellationToken cancellationToken = default)
    {
        return client.StopStimulationAsync(targetAddress, options, cancellationToken);
    }

    public Task<BackplaneStimulationStatusResult> ReadStatusAsync(
        byte targetAddress,
        BackplaneConnectionOptions options,
        CancellationToken cancellationToken = default)
    {
        return client.ReadStimulationStatusAsync(targetAddress, options, cancellationToken);
    }
}
