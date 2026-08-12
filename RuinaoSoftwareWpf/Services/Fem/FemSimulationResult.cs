namespace RuinaoSoftwareWpf;

public sealed record FemSimulationResult(
    bool Succeeded,
    int? ExitCode,
    string OutputDirectory,
    string Message,
    TimeSpan Elapsed);
