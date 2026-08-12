namespace RuinaoSoftwareWpf;

public sealed record FemSimulationRequest(
    string WorkerExecutable,
    string InputModelPath,
    string OutputDirectory,
    string Arguments,
    TimeSpan Timeout,
    long MaximumWorkingSetBytes);
