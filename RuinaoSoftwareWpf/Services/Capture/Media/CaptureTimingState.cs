namespace RuinaoSoftwareWpf;

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
    public CameraCaptureProfileSnapshot? CameraProfile { get; private set; }
    public int AttemptedFrameCount { get; private set; }
    public int QueuedFrameCount { get; private set; }
    public int WrittenFrameCount { get; private set; }
    public int DroppedFrameCount { get; private set; }
    public int MaximumQueueDepth { get; private set; }
    public double DroppedFrameRate => AttemptedFrameCount > 0
        ? DroppedFrameCount / (double)AttemptedFrameCount
        : 0;
    public long? AudioStartDelayFromRecordMs => AudioStartedAt.HasValue ? (long)(AudioStartedAt.Value - RecordStartedAt).TotalMilliseconds : null;
    public long? AudioStartDelayFromFirstFrameMs => AudioStartedAt.HasValue && FirstFrameAt.HasValue ? (long)(AudioStartedAt.Value - FirstFrameAt.Value).TotalMilliseconds : null;
    public long? AudioStartDelayFromFirstFrameWrittenMs => AudioStartedAt.HasValue && FirstFrameWrittenAt.HasValue ? (long)(AudioStartedAt.Value - FirstFrameWrittenAt.Value).TotalMilliseconds : null;
    public long? AudioTailAfterLastFrameWrittenMs => AudioStoppedAt.HasValue && LastFrameWrittenAt.HasValue ? (long)(AudioStoppedAt.Value - LastFrameWrittenAt.Value).TotalMilliseconds : null;

    public void RecordFrame(DateTimeOffset frameAt, int queuedFrameCount)
    {
        FirstFrameAt ??= frameAt;
        LastFrameAt = frameAt;
        QueuedFrameCount = queuedFrameCount;
    }

    public void RecordFrameAttempt(int queueDepth)
    {
        AttemptedFrameCount++;
        MaximumQueueDepth = Math.Max(MaximumQueueDepth, queueDepth);
    }

    public void RecordFrameDropped(int queueDepth)
    {
        DroppedFrameCount++;
        MaximumQueueDepth = Math.Max(MaximumQueueDepth, queueDepth);
    }

    public void RecordFrameWritten(DateTimeOffset writtenAt, int writtenFrameCount)
    {
        FirstFrameWrittenAt ??= writtenAt;
        LastFrameWrittenAt = writtenAt;
        WrittenFrameCount = writtenFrameCount;
    }

    public void RecordAudioStarted(DateTimeOffset audioStartedAt, DateTimeOffset firstFrameAt, string reason)
    {
        AudioStartedAt ??= audioStartedAt;
        FirstFrameAt ??= firstFrameAt;
        AudioStartReason ??= reason;
    }

    public void RecordAudioStopped(DateTimeOffset audioStoppedAt) => AudioStoppedAt ??= audioStoppedAt;
    public void RecordRawVideoPath(string rawVideoPath) => RawVideoPath = rawVideoPath;
    public void RecordCameraProfile(CameraCaptureProfileSnapshot? profile) => CameraProfile = profile;
    public void RecordAdjustedFrameRate(double? adjustedFrameRate) => AdjustedVideoFrameRate = adjustedFrameRate;

    public void Complete(DateTimeOffset endedAt, int queuedFrameCount)
    {
        RecordEndedAt = endedAt;
        QueuedFrameCount = queuedFrameCount;
    }
}
