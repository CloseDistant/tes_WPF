namespace RuinaoSoftwareWpf.Tests;

using System.Security;
using Xunit;

public sealed class TrustedExecutablePathTests
{
    [Fact]
    public void CaptureMediaEncoder_UsesOnlyBundledFfmpegTools()
    {
        using var directory = new TemporaryDirectory();
        var ffmpegPath = directory.WriteTool("ffmpeg/ffmpeg.exe");
        var ffprobePath = directory.WriteTool("ffmpeg/ffprobe.exe");

        Assert.Equal(ffmpegPath, CaptureMediaEncoder.ResolveFfmpegPath(directory.Path));
        Assert.Equal(ffprobePath, CaptureMediaEncoder.ResolveFfprobePath(directory.Path));
    }

    [Theory]
    [InlineData("../outside.exe")]
    [InlineData("ffmpeg/not-an-executable.dll")]
    public void RequireBundledTool_RejectsPathsOutsideTrustedExecutableScope(string relativePath)
    {
        using var directory = new TemporaryDirectory();

        Assert.Throws<SecurityException>(() =>
            TrustedExecutablePath.RequireBundledTool(relativePath, directory.Path));
    }

    [Fact]
    public void RequireTrustedToolPath_RejectsAbsoluteExecutableOutsideApplicationTools()
    {
        using var applicationDirectory = new TemporaryDirectory();
        using var outsideDirectory = new TemporaryDirectory();
        var outsideExecutable = outsideDirectory.WriteRootFile("worker.exe");

        Assert.Throws<SecurityException>(() =>
            TrustedExecutablePath.RequireTrustedToolPath(outsideExecutable, applicationDirectory.Path));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"ruinao-tools-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string WriteTool(string relativePath)
        {
            var fullPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(Path, "Tools", relativePath));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(fullPath)!);
            File.WriteAllBytes(fullPath, [0x4d, 0x5a]);
            return fullPath;
        }

        public string WriteRootFile(string fileName)
        {
            var fullPath = System.IO.Path.Combine(Path, fileName);
            File.WriteAllBytes(fullPath, [0x4d, 0x5a]);
            return fullPath;
        }

        public void Dispose()
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
