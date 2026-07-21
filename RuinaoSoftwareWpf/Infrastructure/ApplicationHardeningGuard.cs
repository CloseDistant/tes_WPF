namespace RuinaoSoftwareWpf;

using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

internal sealed record ReleaseIntegrityResult(bool IsValid, string ErrorCode, int VerifiedFileCount = 0)
{
    public static ReleaseIntegrityResult Success { get; } = new(true, string.Empty);

    public static ReleaseIntegrityResult Failure(string errorCode) => new(false, errorCode);
}

/// <summary>
/// Verifies the authenticated Release file catalog before application services are created.
/// </summary>
internal static class ApplicationHardeningGuard
{
    internal const string ManifestFileName = "release-integrity.manifest";
    internal const string ManifestHeader = "# ruinao-release-integrity-v1";
    private const string ManifestAuthenticationKeyHex =
        "E50E19CE92051AE1297D82D9B1F57E93A80406D9077E35705F50E141D7EF6428";

    public static ReleaseIntegrityResult VerifyCurrentRelease()
    {
#if DEBUG
        return ReleaseIntegrityResult.Success;
#else
        return VerifyDirectory(AppContext.BaseDirectory);
#endif
    }

    internal static ReleaseIntegrityResult VerifyDirectory(string applicationDirectory)
        => VerifyDirectory(applicationDirectory, null, CancellationToken.None);

    internal static Task<ReleaseIntegrityResult> VerifyDirectoryAsync(
        string applicationDirectory,
        IProgress<IntegrityCheckProgress>? progress,
        CancellationToken cancellationToken)
    {
        return Task.Run(
            () => VerifyDirectory(applicationDirectory, progress, cancellationToken),
            cancellationToken);
    }

    private static ReleaseIntegrityResult VerifyDirectory(
        string applicationDirectory,
        IProgress<IntegrityCheckProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(applicationDirectory))
        {
            return ReleaseIntegrityResult.Failure("invalid-application-directory");
        }

        var root = Path.GetFullPath(applicationDirectory);
        var manifestPath = Path.Combine(root, ManifestFileName);
        if (!File.Exists(manifestPath))
        {
            return ReleaseIntegrityResult.Failure("manifest-missing");
        }

        string[] lines;
        try
        {
            lines = File.ReadAllLines(manifestPath, Encoding.UTF8);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ReleaseIntegrityResult.Failure("manifest-unreadable");
        }

        if (lines.Length < 4
            || !string.Equals(lines[0], ManifestHeader, StringComparison.Ordinal)
            || !lines[1].StartsWith("version=", StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(lines[1]["version=".Length..])
            || !lines[^1].StartsWith("hmac=", StringComparison.Ordinal))
        {
            return ReleaseIntegrityResult.Failure("manifest-format-invalid");
        }

        var expectedHmacText = lines[^1]["hmac=".Length..];
        if (!TryDecodeHex(expectedHmacText, out var expectedHmac))
        {
            return ReleaseIntegrityResult.Failure("manifest-authentication-invalid");
        }

        var authenticatedLines = lines[..^1];
        var payload = string.Join('\n', authenticatedLines);
        var actualHmac = ComputeManifestHmac(payload);
        if (!CryptographicOperations.FixedTimeEquals(actualHmac, expectedHmac))
        {
            return ReleaseIntegrityResult.Failure("manifest-authentication-failed");
        }

        var expectedFiles = new Dictionary<string, ManifestFileEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in authenticatedLines.Skip(2))
        {
            if (!line.StartsWith("file=", StringComparison.Ordinal))
            {
                return ReleaseIntegrityResult.Failure("manifest-entry-invalid");
            }

            var fields = line["file=".Length..].Split('|');
            if (fields.Length != 3
                || !long.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out var length)
                || length < 0
                || !TryDecodeHex(fields[2], out var hash)
                || hash.Length != 32)
            {
                return ReleaseIntegrityResult.Failure("manifest-entry-invalid");
            }

            var relativePath = fields[0].Replace('/', Path.DirectorySeparatorChar);
            if (!TryResolveManifestPath(root, relativePath, out var fullPath)
                || !IsExecutableCodeFile(fullPath))
            {
                return ReleaseIntegrityResult.Failure("manifest-path-invalid");
            }

            var normalizedRelativePath = Path.GetRelativePath(root, fullPath)
                .Replace(Path.DirectorySeparatorChar, '/');
            if (!expectedFiles.TryAdd(normalizedRelativePath, new ManifestFileEntry(fullPath, length, hash)))
            {
                return ReleaseIntegrityResult.Failure("manifest-entry-duplicate");
            }
        }

        if (expectedFiles.Count == 0)
        {
            return ReleaseIntegrityResult.Failure("manifest-empty");
        }

        var totalBytes = expectedFiles.Values.Sum(item => Math.Max(1L, item.Length));
        long verifiedBytes = 0;
        foreach (var pair in expectedFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = pair.Value;
            if (!File.Exists(entry.FullPath))
            {
                return ReleaseIntegrityResult.Failure("file-missing");
            }

            try
            {
                var fileInfo = new FileInfo(entry.FullPath);
                if (fileInfo.Length != entry.Length)
                {
                    return ReleaseIntegrityResult.Failure("file-size-mismatch");
                }

                using var stream = File.Open(entry.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[1024 * 1024];
                int read;
                while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    hash.AppendData(buffer, 0, read);
                    verifiedBytes += read;
                    progress?.Report(new IntegrityCheckProgress(
                        "发布文件",
                        pair.Key,
                        verifiedBytes,
                        totalBytes));
                }

                var actualHash = hash.GetHashAndReset();
                if (!CryptographicOperations.FixedTimeEquals(actualHash, entry.Sha256))
                {
                    return ReleaseIntegrityResult.Failure("file-hash-mismatch");
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return ReleaseIntegrityResult.Failure("file-unreadable");
            }
        }

        HashSet<string> actualFiles;
        try
        {
            actualFiles = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Where(path => IsExecutableCodeFile(path) && !IsNestedPublishOutput(root, path))
                .Select(path => Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/'))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ReleaseIntegrityResult.Failure("application-directory-unreadable");
        }

        if (!actualFiles.SetEquals(expectedFiles.Keys))
        {
            return ReleaseIntegrityResult.Failure("file-set-mismatch");
        }

        progress?.Report(new IntegrityCheckProgress("发布文件", "校验完成", totalBytes, totalBytes));
        return new ReleaseIntegrityResult(true, string.Empty, expectedFiles.Count);
    }

    internal static string CreateManifestContent(
        string version,
        IEnumerable<(string RelativePath, long Length, byte[] Sha256)> files)
    {
        var lines = new List<string>
        {
            ManifestHeader,
            $"version={version}"
        };
        lines.AddRange(files
            .OrderBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase)
            .Select(item =>
                $"file={item.RelativePath.Replace('\\', '/')}|{item.Length.ToString(CultureInfo.InvariantCulture)}|{Convert.ToHexString(item.Sha256)}"));

        var payload = string.Join('\n', lines);
        lines.Add($"hmac={Convert.ToHexString(ComputeManifestHmac(payload))}");
        return string.Join('\n', lines) + "\n";
    }

    private static byte[] ComputeManifestHmac(string payload)
    {
        using var hmac = new HMACSHA256(Convert.FromHexString(ManifestAuthenticationKeyHex));
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
    }

    private static bool TryResolveManifestPath(string root, string relativePath, out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
        {
            return false;
        }

        var rootWithSeparator = Path.EndsInDirectorySeparator(root)
            ? root
            : root + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(Path.Combine(root, relativePath));
        if (!candidate.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        fullPath = candidate;
        return true;
    }

    private static bool IsExecutableCodeFile(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".exe", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".dll", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsNestedPublishOutput(string root, string path)
    {
        var relativePath = Path.GetRelativePath(root, path);
        var firstSeparator = relativePath.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]);
        var firstSegment = firstSeparator < 0 ? relativePath : relativePath[..firstSeparator];
        return firstSegment.Equals("publish", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryDecodeHex(string value, out byte[] bytes)
    {
        try
        {
            bytes = Convert.FromHexString(value);
            return true;
        }
        catch (FormatException)
        {
            bytes = [];
            return false;
        }
    }

    private sealed record ManifestFileEntry(string FullPath, long Length, byte[] Sha256);
}
