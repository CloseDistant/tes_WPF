namespace RuinaoSoftwareWpf;

public interface IIntegrityCheckService
{
    Task<ReleaseIntegrityStatus> GetReleaseStatusAsync(
        CancellationToken cancellationToken = default);

    Task<IntegrityCheckResult> CheckReleaseFilesAsync(
        IProgress<IntegrityCheckProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
