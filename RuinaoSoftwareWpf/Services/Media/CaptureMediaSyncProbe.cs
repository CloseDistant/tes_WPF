namespace RuinaoSoftwareWpf;

using System.Diagnostics;
using System.Globalization;
using System.IO;

internal interface ICaptureMediaSyncProbe
{
    Task<MediaSyncProbeResult> ProbeAsync(
        CaptureSessionInfo session,
        CaptureTimingState timing,
        int writtenFrameCount);
}

internal sealed class CaptureMediaSyncProbe : ICaptureMediaSyncProbe
{
    private const long SyncWarningThresholdMs = 1000;

    public async Task<MediaSyncProbeResult> ProbeAsync(
        CaptureSessionInfo session,
        CaptureTimingState timing,
        int writtenFrameCount)
    {
        var rawVideoDurationMs = await ProbeDurationMsAsync(session.RawVideoPath);
        var normalizedVideoDurationMs = await ProbeDurationMsAsync(session.NormalizedVideoPath);
        var rawAudioDurationMs = await ProbeDurationMsAsync(session.AudioPath);
        var mergedDurationMs = await ProbeDurationMsAsync(session.MergedVideoPath);
        long? syncOffsetMs = rawAudioDurationMs.HasValue && mergedDurationMs.HasValue ? Math.Abs(rawAudioDurationMs.Value - mergedDurationMs.Value) : null;
        long? rawContainerDurationOffsetMs = rawAudioDurationMs.HasValue && rawVideoDurationMs.HasValue ? Math.Abs(rawAudioDurationMs.Value - rawVideoDurationMs.Value) : null;
        long? realDurationMs = timing.RecordEndedAt.HasValue ? (long)(timing.RecordEndedAt.Value - timing.RecordStartedAt).TotalMilliseconds : null;
        var status = syncOffsetMs.HasValue && syncOffsetMs.Value > SyncWarningThresholdMs ? "warning" : "ok";

        return new MediaSyncProbeResult(
            status, syncOffsetMs, SyncWarningThresholdMs, rawVideoDurationMs, normalizedVideoDurationMs,
            session.RawVideoPath, session.NormalizedVideoPath, rawAudioDurationMs, mergedDurationMs, realDurationMs,
            timing.RecordStartedAt.ToUnixTimeMilliseconds(), timing.RecordEndedAt?.ToUnixTimeMilliseconds(),
            timing.FirstFrameAt?.ToUnixTimeMilliseconds(), timing.LastFrameAt?.ToUnixTimeMilliseconds(),
            timing.FirstFrameWrittenAt?.ToUnixTimeMilliseconds(), timing.LastFrameWrittenAt?.ToUnixTimeMilliseconds(),
            timing.AudioStartedAt?.ToUnixTimeMilliseconds(), timing.AudioStoppedAt?.ToUnixTimeMilliseconds(),
            timing.AudioStartReason, timing.AudioStartDelayFromRecordMs, timing.AudioStartDelayFromFirstFrameWrittenMs,
            timing.AudioTailAfterLastFrameWrittenMs, timing.AdjustedVideoFrameRate, rawContainerDurationOffsetMs,
            timing.QueuedFrameCount, timing.WrittenFrameCount, writtenFrameCount,
            FileSize(session.RawVideoPath), FileSize(session.NormalizedVideoPath), FileSize(session.AudioPath), FileSize(session.MergedVideoPath));
    }

    internal static async Task<long?> ProbeDurationMsAsync(string mediaPath)
    {
        if (!File.Exists(mediaPath))
        {
            return null;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = CaptureMediaEncoder.ResolveFfprobePath(),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        foreach (var argument in new[] { "-v", "error", "-show_entries", "format=duration", "-of", "default=noprint_wrappers=1:nokey=1", mediaPath })
        {
            startInfo.ArgumentList.Add(argument);
        }

        var result = await ExternalProcessRunner.RunAsync(startInfo, TimeSpan.FromSeconds(30));
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(result.StandardError)
                ? "FFprobe 读取媒体时长失败"
                : result.StandardError.Trim());
        }

        return double.TryParse(result.StandardOutput.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
            ? (long)Math.Round(seconds * 1000d)
            : null;
    }

    private static long? FileSize(string path) => File.Exists(path) ? new FileInfo(path).Length : null;
}
