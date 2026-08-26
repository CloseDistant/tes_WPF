namespace RuinaoSoftwareWpf;

public enum FaceReadinessState
{
    NotReady,
    Stabilizing,
    Ready
}

public readonly record struct FaceReadinessUpdate(
    FaceReadinessState State,
    TimeSpan StableDuration,
    TimeSpan RemainingDuration,
    double ProgressPercent)
{
    public bool IsReady => State == FaceReadinessState.Ready;
}

/// <summary>
/// 使用单调时钟确认开始采集前的人脸状态；任何不满足条件的帧都会清空连续稳定时间。
/// </summary>
public sealed class FaceReadinessMonitor
{
    private readonly TimeSpan confirmationDuration;
    private readonly long confirmationTicks;
    private readonly long timestampFrequency;
    private long? stableStartedAt;

    public FaceReadinessMonitor(TimeSpan confirmationDuration, long timestampFrequency)
    {
        if (confirmationDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(confirmationDuration));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timestampFrequency);
        this.confirmationDuration = confirmationDuration;
        this.timestampFrequency = timestampFrequency;
        confirmationTicks = Math.Max(
            1,
            (long)Math.Ceiling(confirmationDuration.TotalSeconds * timestampFrequency));
    }

    public FaceReadinessUpdate Observe(bool meetsRequirements, long timestamp)
    {
        if (!meetsRequirements)
        {
            Reset();
            return new FaceReadinessUpdate(
                FaceReadinessState.NotReady,
                TimeSpan.Zero,
                confirmationDuration,
                0);
        }

        stableStartedAt ??= timestamp;
        var elapsedTicks = Math.Clamp(timestamp - stableStartedAt.Value, 0, confirmationTicks);
        var stableDuration = TimeSpan.FromSeconds(elapsedTicks / (double)timestampFrequency);
        var remainingDuration = confirmationDuration - stableDuration;
        var progressPercent = elapsedTicks / (double)confirmationTicks * 100d;
        var state = elapsedTicks >= confirmationTicks
            ? FaceReadinessState.Ready
            : FaceReadinessState.Stabilizing;

        return new FaceReadinessUpdate(
            state,
            stableDuration,
            remainingDuration < TimeSpan.Zero ? TimeSpan.Zero : remainingDuration,
            progressPercent);
    }

    public void Reset() => stableStartedAt = null;
}
