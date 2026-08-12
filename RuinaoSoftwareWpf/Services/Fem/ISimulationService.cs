namespace RuinaoSoftwareWpf;

public interface ISimulationService
{
    bool IsRunning { get; }

    Task<FemSimulationResult> RunAsync(
        FemSimulationRequest request,
        IProgress<FemSimulationProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task CancelAsync();
}
