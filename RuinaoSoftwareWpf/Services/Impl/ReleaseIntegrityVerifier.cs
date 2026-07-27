namespace RuinaoSoftwareWpf;

using System.IO;
using System.Security.Cryptography;

internal sealed class ReleaseIntegrityVerifier : IReleaseIntegrityVerifier
{
    public ReleaseIntegrityVerifier()
    {
    }

    public Task<ReleaseIntegrityResult> VerifyAsync(
        IProgress<IntegrityCheckProgress>? progress,
        CancellationToken cancellationToken)
    {
        return ApplicationHardeningGuard.VerifyDirectoryAsync(
            AppContext.BaseDirectory,
            progress,
            cancellationToken);
    }

    public async Task<string?> GetManifestIdentityAsync(CancellationToken cancellationToken)
    {
        var manifestPath = Path.Combine(
            AppContext.BaseDirectory,
            ApplicationHardeningGuard.ManifestFileName);
        try
        {
            await using var stream = new FileStream(
                manifestPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var hash = await SHA256.HashDataAsync(stream, cancellationToken);
            return Convert.ToHexString(hash);
        }
        catch (Exception exception) when (
            exception is FileNotFoundException
                or DirectoryNotFoundException
                or IOException
                or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
