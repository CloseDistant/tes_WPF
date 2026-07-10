namespace RuinaoHardwareDebugWpf;

internal sealed class CaptureTimingState
{
    public CaptureTimingState(DateTimeOffset recordStartedAt)
    {
        RecordStartedAt = recordStartedAt;
    }

    public DateTimeOffset RecordStartedAt { get; }
    public DateTimeOffset? RecordEndedAt { get; private set; }
    public DateTimeOffset? FirstFrameAt { get; private set; }
    public DateTimeOffset? LastFrameAt { get; private set; }
    public DateTimeOffset? FirstFrameWrittenAt { get; private set; }
    public DateTimeOffset? LastFrameWrittenAt { get; private set; }
    public DateTimeOffset? AudioStartedAt { get; private set; }
    public DateTimeOffset? AudioStoppedAt { get; private set; }
    public string? AudioStartReason { get; private set; }
    public string? RawVideoPath { get; private set; }
    public double? AdjustedVideoFrameRate { get; private set; }
    public int QueuedFrameCount { get; private set; }
    public int WrittenFrameCount { get; private set; }
    public long? AudioStartDelayFromRecordMs => AudioStartedAt.HasValue ? (long)(AudioStartedAt.Value - RecordStartedAt).TotalMilliseconds : null;
    public long? AudioStartDelayFromFirstFrameWrittenMs => AudioStartedAt.HasValue && FirstFrameWrittenAt.HasValue ? (long)(AudioStartedAt.Value - FirstFrameWrittenAt.Value).TotalMilliseconds : null;
    public long? AudioTailAfterLastFrameWrittenMs => AudioStoppedAt.HasValue && LastFrameWrittenAt.HasValue ? (long)(AudioStoppedAt.Value - LastFrameWrittenAt.Value).TotalMilliseconds : null;

    public void RecordFrame(DateTimeOffset frameAt, int queuedFrameCount)
    {
        FirstFrameAt ??= frameAt;
        LastFrameAt = frameAt;
        QueuedFrameCount = queuedFrameCount;
    }

    public void RecordFrameWritten(DateTimeOffset writtenAt, int writtenFrameCount)
    {
        FirstFrameWrittenAt ??= writtenAt;
        LastFrameWrittenAt = writtenAt;
        WrittenFrameCount = writtenFrameCount;
    }

    public void RecordAudioStarted(DateTimeOffset audioStartedAt, DateTimeOffset firstFrameWrittenAt, string reason)
    {
        AudioStartedAt ??= audioStartedAt;
        FirstFrameWrittenAt ??= firstFrameWrittenAt;
        AudioStartReason ??= reason;
    }

    public void RecordAudioStopped(DateTimeOffset audioStoppedAt) => AudioStoppedAt ??= audioStoppedAt;
    public void RecordRawVideoPath(string rawVideoPath) => RawVideoPath = rawVideoPath;
    public void RecordAdjustedFrameRate(double? adjustedFrameRate) => AdjustedVideoFrameRate = adjustedFrameRate;

    public void Complete(DateTimeOffset endedAt, int queuedFrameCount)
    {
        RecordEndedAt = endedAt;
        QueuedFrameCount = queuedFrameCount;
    }
}

internal sealed record MediaSyncProbeResult(
    string SyncStatus,
    long? SyncOffsetMs,
    long SyncWarningThresholdMs,
    long? RawVideoDurationMs,
    long? NormalizedVideoDurationMs,
    string? RawVideoPath,
    string? NormalizedVideoPath,
    long? RawAudioDurationMs,
    long? MergedDurationMs,
    long? RealRecordDurationMs,
    long? RecordStartedAtUnixMs,
    long? RecordEndedAtUnixMs,
    long? FirstFrameAtUnixMs,
    long? LastFrameAtUnixMs,
    long? FirstFrameWrittenAtUnixMs,
    long? LastFrameWrittenAtUnixMs,
    long? AudioStartedAtUnixMs,
    long? AudioStoppedAtUnixMs,
    string? AudioStartReason,
    long? AudioStartDelayFromRecordMs,
    long? AudioStartDelayFromFirstFrameWrittenMs,
    long? AudioTailAfterLastFrameWrittenMs,
    double? AdjustedVideoFrameRate,
    long? RawContainerDurationOffsetMs,
    int QueuedFrameCount,
    int TimingWrittenFrameCount,
    int WrittenFrameCount,
    long? RawVideoSizeBytes,
    long? NormalizedVideoSizeBytes,
    long? RawAudioSizeBytes,
    long? MergedVideoSizeBytes);
