namespace RuinaoSoftwareWpf;

/// <summary>
/// 以固定时间轴选择最接近目标频率的输入帧；输入偶尔迟到时不会把误差累积到后续帧。
/// </summary>
internal sealed class FixedIntervalFrameSampler
{
    private readonly long intervalTicks;
    private readonly long earlyToleranceTicks;
    private long nextSampleTimestamp = long.MinValue;

    public FixedIntervalFrameSampler(
        TimeSpan interval,
        long timestampFrequency,
        double earlyToleranceRatio = 0)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(interval, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timestampFrequency, 0);
        ArgumentOutOfRangeException.ThrowIfNegative(earlyToleranceRatio);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(earlyToleranceRatio, 0.49);
        intervalTicks = Math.Max(1, (long)Math.Round(interval.TotalSeconds * timestampFrequency));
        earlyToleranceTicks = (long)Math.Round(intervalTicks * earlyToleranceRatio);
    }

    public bool ShouldSample(long timestamp)
    {
        if (nextSampleTimestamp == long.MinValue)
        {
            nextSampleTimestamp = checked(timestamp + intervalTicks);
            return true;
        }

        if (timestamp + earlyToleranceTicks < nextSampleTimestamp)
        {
            return false;
        }

        var elapsedIntervals = timestamp < nextSampleTimestamp
            ? 1
            : (timestamp - nextSampleTimestamp) / intervalTicks + 1;
        nextSampleTimestamp = checked(nextSampleTimestamp + elapsedIntervals * intervalTicks);
        return true;
    }
}
