namespace RuinaoSoftwareWpf;

public sealed record BackupOperationResult(bool Succeeded, string Message, string? FilePath = null);
