namespace RuinaoHardwareDebugWpf;

public sealed record FemSimulationRequest(
    string WorkerExecutable,
    string InputModelPath,
    string OutputDirectory,
    string Arguments,
    TimeSpan Timeout,
    long MaximumWorkingSetBytes);

public sealed record FemSimulationProgress(double Percent, string Stage, string Message);

public sealed record FemSimulationResult(
    bool Succeeded,
    int? ExitCode,
    string OutputDirectory,
    string Message,
    TimeSpan Elapsed);

public interface ISimulationService
{
    bool IsRunning { get; }

    Task<FemSimulationResult> RunAsync(
        FemSimulationRequest request,
        IProgress<FemSimulationProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task CancelAsync();
}
