namespace RuinaoSoftwareWpf;

internal sealed record RestoreFileState(string TargetPath, string RollbackPath, bool OriginallyExisted);
