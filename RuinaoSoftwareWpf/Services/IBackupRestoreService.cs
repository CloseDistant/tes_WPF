namespace RuinaoSoftwareWpf;

public interface IBackupRestoreService
{
    Task<BackupLocationInfo> GetDefaultBackupLocationAsync(CancellationToken cancellationToken = default);

    Task<BackupStatus> GetStatusAsync(CancellationToken cancellationToken = default);

    Task<BackupOperationResult> CreateBackupAsync(
        string directoryPath,
        string password,
        IProgress<BackupOperationProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<BackupOperationResult> RestoreBackupAsync(
        string backupFilePath,
        string password,
        IProgress<BackupOperationProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
