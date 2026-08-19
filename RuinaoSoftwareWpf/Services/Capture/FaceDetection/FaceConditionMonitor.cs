namespace RuinaoSoftwareWpf;

public readonly record struct FaceConditionMonitorUpdate(
    CameraFaceState State,
    TimeSpan AbnormalDuration,
    bool JustConfirmed)
{
    public bool IsNormal => State == CameraFaceState.Normal;
}

/// <summary>
/// 使用单调时钟累计连续异常时间；异常原因变化不会清零，恢复正常才清零。
/// </summary>
public sealed class FaceConditionMonitor
{
    private readonly long confirmationTicks;
    private readonly long timestampFrequency;
    private long? abnormalStartedAt;
    private bool confirmed;

    public FaceConditionMonitor(TimeSpan confirmationDuration, long timestampFrequency)
    {
        if (confirmationDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(confirmationDuration));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timestampFrequency);
        this.timestampFrequency = timestampFrequency;
        confirmationTicks = Math.Max(
            1,
            (long)Math.Ceiling(confirmationDuration.TotalSeconds * timestampFrequency));
    }

    public FaceConditionMonitorUpdate Observe(CameraFaceState state, long timestamp)
    {
        if (state == CameraFaceState.Normal)
        {
            Reset();
            return new FaceConditionMonitorUpdate(state, TimeSpan.Zero, false);
        }

        abnormalStartedAt ??= timestamp;
        var elapsedTicks = Math.Max(0, timestamp - abnormalStartedAt.Value);
        var justConfirmed = !confirmed && elapsedTicks >= confirmationTicks;
        if (justConfirmed)
        {
            confirmed = true;
        }

        return new FaceConditionMonitorUpdate(
            state,
            TimeSpan.FromSeconds(elapsedTicks / (double)timestampFrequency),
            justConfirmed);
    }

    public void Reset()
    {
        abnormalStartedAt = null;
        confirmed = false;
    }
}
