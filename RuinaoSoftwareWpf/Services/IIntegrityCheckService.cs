namespace RuinaoSoftwareWpf;

public interface IIntegrityCheckService
{
    Task<IntegrityCheckResult> CheckReleaseFilesAsync(
        IProgress<IntegrityCheckProgress>? progress = null,
        CancellationToken cancellationToken = default);

}
