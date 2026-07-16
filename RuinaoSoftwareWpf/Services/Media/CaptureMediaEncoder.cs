namespace RuinaoSoftwareWpf;

using System.Diagnostics;
using System.Globalization;
using System.IO;

internal interface ICaptureMediaEncoder
{
    void WaitForFileReady(string filePath);
    Task<double?> CalculateAdjustedFrameRateAsync(string audioPath, int writtenFrameCount);
    Task NormalizeVideoDurationAsync(string rawVideoPath, string normalizedVideoPath, double? adjustedFrameRate);
    Task MergeAsync(string videoPath, string audioPath, string outputPath);
    void DeleteDiscardedRecording(CaptureSessionInfo session);
}

internal sealed class CaptureMediaEncoder : ICaptureMediaEncoder
{
    public void WaitForFileReady(string filePath)
    {
        for (var index = 0; index < 30; index++)
        {
            try
            {
                if (File.Exists(filePath) && new FileInfo(filePath).Length > 0)
                {
                    using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    return;
                }
            }
            catch (IOException)
            {
            }

            Thread.Sleep(100);
        }

        throw new IOException($"文件尚未准备完成：{filePath}");
    }

    public async Task<double?> CalculateAdjustedFrameRateAsync(string audioPath, int writtenFrameCount)
    {
        if (writtenFrameCount <= 1)
        {
            return null;
        }

        var audioDurationMs = await CaptureMediaSyncProbe.ProbeDurationMsAsync(audioPath);
        if (!audioDurationMs.HasValue || audioDurationMs.Value <= 0)
        {
            return null;
        }

        var frameRate = writtenFrameCount / (audioDurationMs.Value / 1000d);
        return frameRate is >= 1d and <= 60d ? frameRate : null;
    }

    public async Task NormalizeVideoDurationAsync(string rawVideoPath, string normalizedVideoPath, double? adjustedFrameRate)
    {
        if (!adjustedFrameRate.HasValue)
        {
            File.Copy(rawVideoPath, normalizedVideoPath, overwrite: true);
            return;
        }

        var startInfo = CreateProcessStartInfo(ResolveFfmpegPath());
        startInfo.ArgumentList.Add("-y");
        startInfo.ArgumentList.Add("-r");
        startInfo.ArgumentList.Add(adjustedFrameRate.Value.ToString("0.###", CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("-i");
        startInfo.ArgumentList.Add(rawVideoPath);
        startInfo.ArgumentList.Add("-c:v");
        startInfo.ArgumentList.Add("mjpeg");
        startInfo.ArgumentList.Add("-q:v");
        startInfo.ArgumentList.Add("3");
        startInfo.ArgumentList.Add(normalizedVideoPath);
        var result = await ExternalProcessRunner.RunAsync(startInfo, TimeSpan.FromMinutes(2));
        if (result.ExitCode != 0 || !File.Exists(normalizedVideoPath))
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(result.StandardError)
                ? "OpenCV 原始视频时长校正失败"
                : result.StandardError.Trim());
        }
    }

    public async Task MergeAsync(string videoPath, string audioPath, string outputPath)
    {
        var startInfo = CreateProcessStartInfo(ResolveFfmpegPath());
        foreach (var argument in new[]
        {
            "-y", "-i", videoPath, "-i", audioPath, "-map", "0:v:0", "-map", "1:a:0",
            "-c:v", "libx264", "-preset", "veryfast", "-pix_fmt", "yuv420p", "-c:a", "aac", outputPath
        })
        {
            startInfo.ArgumentList.Add(argument);
        }

        var result = await ExternalProcessRunner.RunAsync(startInfo, TimeSpan.FromMinutes(5));
        if (result.ExitCode != 0 || !File.Exists(outputPath))
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(result.StandardError)
                ? "FFmpeg 合成失败"
                : result.StandardError.Trim());
        }
    }

    public void DeleteDiscardedRecording(CaptureSessionInfo session)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                DeleteFile(session.RawVideoPath);
                DeleteFile(session.NormalizedVideoPath);
                DeleteFile(session.AudioPath);
                DeleteFile(session.MergedVideoPath);
                if (Directory.Exists(session.OutputDirectory) && !Directory.EnumerateFileSystemEntries(session.OutputDirectory).Any())
                {
                    Directory.Delete(session.OutputDirectory);
                }

                return;
            }
            catch (IOException) { Thread.Sleep(150); }
            catch (UnauthorizedAccessException) { Thread.Sleep(150); }
        }
    }

    internal static string ResolveFfmpegPath()
    {
        var environmentPath = Environment.GetEnvironmentVariable("RUINAO_FFMPEG_PATH");
        if (!string.IsNullOrWhiteSpace(environmentPath) && File.Exists(environmentPath))
        {
            return environmentPath;
        }

        var baseDirectory = AppContext.BaseDirectory;
        return new[]
        {
            Path.Combine(baseDirectory, "Tools", "ffmpeg", "ffmpeg.exe"),
            Path.Combine(baseDirectory, "tools", "ffmpeg", "ffmpeg.exe"),
            Path.Combine(baseDirectory, "ffmpeg.exe")
        }.FirstOrDefault(File.Exists) ?? "ffmpeg";
    }

    internal static string ResolveFfprobePath()
    {
        var environmentPath = Environment.GetEnvironmentVariable("RUINAO_FFPROBE_PATH");
        if (!string.IsNullOrWhiteSpace(environmentPath) && File.Exists(environmentPath))
        {
            return environmentPath;
        }

        var directory = Path.GetDirectoryName(ResolveFfmpegPath());
        var sibling = string.IsNullOrWhiteSpace(directory) ? null : Path.Combine(directory, "ffprobe.exe");
        if (sibling is not null && File.Exists(sibling))
        {
            return sibling;
        }

        var baseDirectory = AppContext.BaseDirectory;
        return new[]
        {
            Path.Combine(baseDirectory, "Tools", "ffmpeg", "ffprobe.exe"),
            Path.Combine(baseDirectory, "tools", "ffmpeg", "ffprobe.exe"),
            Path.Combine(baseDirectory, "ffprobe.exe")
        }.FirstOrDefault(File.Exists) ?? "ffprobe";
    }

    private static ProcessStartInfo CreateProcessStartInfo(string fileName) => new()
    {
        FileName = fileName,
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardError = true,
        RedirectStandardOutput = true
    };

    private static void DeleteFile(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
