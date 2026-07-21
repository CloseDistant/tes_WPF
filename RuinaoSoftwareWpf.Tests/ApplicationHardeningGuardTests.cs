namespace RuinaoSoftwareWpf.Tests;

using System.Security.Cryptography;
using System.Text;
using System.Diagnostics;
using Xunit;

public sealed class ApplicationHardeningGuardTests
{
    [Fact]
    public void VerifyDirectory_AcceptsAuthenticatedUntamperedFiles()
    {
        using var directory = new TemporaryDirectory();
        directory.WriteCodeFile("RuinaoSoftwareWpf.exe", "application");
        directory.WriteCodeFile("ApplicationComponent.dll", "component");
        directory.WriteManifest();

        var result = ApplicationHardeningGuard.VerifyDirectory(directory.Path);

        Assert.True(result.IsValid);
        Assert.Equal(string.Empty, result.ErrorCode);
    }

    [Fact]
    public void VerifyDirectory_RejectsModifiedFile()
    {
        using var directory = new TemporaryDirectory();
        directory.WriteCodeFile("RuinaoSoftwareWpf.exe", "application");
        directory.WriteCodeFile("ApplicationComponent.dll", "component");
        directory.WriteManifest();
        directory.WriteCodeFile("ApplicationComponent.dll", "tampered-component");

        var result = ApplicationHardeningGuard.VerifyDirectory(directory.Path);

        Assert.False(result.IsValid);
        Assert.Contains(result.ErrorCode, new[] { "file-size-mismatch", "file-hash-mismatch" });
    }

    [Fact]
    public void VerifyDirectory_RejectsMissingFile()
    {
        using var directory = new TemporaryDirectory();
        directory.WriteCodeFile("RuinaoSoftwareWpf.exe", "application");
        var componentPath = directory.WriteCodeFile("ApplicationComponent.dll", "component");
        directory.WriteManifest();
        File.Delete(componentPath);

        var result = ApplicationHardeningGuard.VerifyDirectory(directory.Path);

        Assert.False(result.IsValid);
        Assert.Equal("file-missing", result.ErrorCode);
    }

    [Fact]
    public void VerifyDirectory_RejectsExecutableAddedAfterManifestCreation()
    {
        using var directory = new TemporaryDirectory();
        directory.WriteCodeFile("RuinaoSoftwareWpf.exe", "application");
        directory.WriteManifest();
        directory.WriteCodeFile("unexpected.dll", "unexpected");

        var result = ApplicationHardeningGuard.VerifyDirectory(directory.Path);

        Assert.False(result.IsValid);
        Assert.Equal("file-set-mismatch", result.ErrorCode);
    }

    [Fact]
    public async Task VerifyDirectoryAsync_ReportsProgressAndAcceptsValidFiles()
    {
        using var directory = new TemporaryDirectory();
        directory.WriteCodeFile("RuinaoSoftwareWpf.exe", new string('a', 32_768));
        directory.WriteCodeFile("ApplicationComponent.dll", new string('b', 16_384));
        directory.WriteManifest();
        var reports = new List<IntegrityCheckProgress>();

        var result = await ApplicationHardeningGuard.VerifyDirectoryAsync(
            directory.Path,
            new InlineProgress<IntegrityCheckProgress>(reports.Add),
            CancellationToken.None);

        Assert.True(result.IsValid);
        Assert.NotEmpty(reports);
        Assert.Equal(100, reports[^1].Percentage);
    }

    [Fact]
    public void ReleaseBuildScript_CreatesManifestAcceptedByRuntimeVerifier()
    {
        using var directory = new TemporaryDirectory();
        directory.WriteCodeFile("RuinaoSoftwareWpf.exe", "application");
        directory.WriteCodeFile("runtimes/win-x64/native/libusbK.dll", "native");
        var scriptPath = FindBuildScript();
        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in new[]
        {
            "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", scriptPath,
            "-OutputDirectory", directory.Path, "-Version", "1.0.0"
        })
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)!;
        process.WaitForExit();
        var standardError = process.StandardError.ReadToEnd();

        Assert.True(process.ExitCode == 0, standardError);
        Assert.True(ApplicationHardeningGuard.VerifyDirectory(directory.Path).IsValid);
    }

    private static string FindBuildScript()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = System.IO.Path.Combine(
                directory.FullName,
                "RuinaoSoftwareWpf",
                "Build",
                "Generate-ReleaseIntegrityManifest.ps1");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("未找到Release完整性清单生成脚本。");
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"ruinao-hardening-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string WriteCodeFile(string relativePath, string content)
        {
            var fullPath = System.IO.Path.Combine(Path, relativePath);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, content, Encoding.UTF8);
            return fullPath;
        }

        public void WriteManifest()
        {
            var files = Directory.EnumerateFiles(Path, "*", SearchOption.AllDirectories)
                .Where(path =>
                    System.IO.Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase)
                    || System.IO.Path.GetExtension(path).Equals(".dll", StringComparison.OrdinalIgnoreCase))
                .Select(path =>
                {
                    var bytes = File.ReadAllBytes(path);
                    return (
                        System.IO.Path.GetRelativePath(Path, path),
                        (long)bytes.Length,
                        SHA256.HashData(bytes));
                });
            var content = ApplicationHardeningGuard.CreateManifestContent("1.0.0", files);
            File.WriteAllText(
                System.IO.Path.Combine(Path, ApplicationHardeningGuard.ManifestFileName),
                content,
                new UTF8Encoding(false));
        }

        public void Dispose()
        {
            Directory.Delete(Path, recursive: true);
        }
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
