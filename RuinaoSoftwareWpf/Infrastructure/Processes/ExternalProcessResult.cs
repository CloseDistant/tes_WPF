namespace RuinaoSoftwareWpf;

internal sealed record ExternalProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError);
