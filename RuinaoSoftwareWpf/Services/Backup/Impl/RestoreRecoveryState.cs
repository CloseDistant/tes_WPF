namespace RuinaoSoftwareWpf;

internal sealed record RestoreRecoveryState(string OperationDirectory, IReadOnlyList<RestoreFileState> Files);
