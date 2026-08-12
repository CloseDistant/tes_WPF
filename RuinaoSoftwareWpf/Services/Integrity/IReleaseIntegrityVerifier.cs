namespace RuinaoSoftwareWpf;

internal interface IReleaseIntegrityVerifier
{
    Task<ReleaseIntegrityResult> VerifyAsync(
        IProgress<IntegrityCheckProgress>? progress,
        CancellationToken cancellationToken);

    Task<string?> GetManifestIdentityAsync(CancellationToken cancellationToken);
}
