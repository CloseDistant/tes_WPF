namespace RuinaoSoftwareWpf;

public sealed record BackupLocationInfo(
    string? DirectoryPath,
    bool IsRemovable,
    long AvailableBytes,
    long EstimatedBytes,
    string Message);

public sealed record BackupStatus(
    DateTimeOffset? LastBackupAt,
    string? LastBackupFileName);

public sealed record BackupOperationProgress(
    string Stage,
    int Percentage,
    string Detail);

public sealed record BackupOperationResult(
    bool Succeeded,
    string Message,
    string? FilePath = null);
