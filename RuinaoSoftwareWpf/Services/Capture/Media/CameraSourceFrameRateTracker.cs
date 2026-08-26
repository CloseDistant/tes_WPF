namespace RuinaoSoftwareWpf;

/// <summary>
/// 根据成功读取帧的单调时间戳统计真实源帧率，避免把驱动属性值当作实际性能。
/// </summary>
internal sealed class CameraSourceFrameRateTracker
{
    private readonly long timestampFrequency;
    private readonly long measurementWindowTicks;
    private long windowStartedAt = long.MinValue;
    private long previousFrameAt = long.MinValue;
    private int frameCount;
    private long maximumGapTicks;

    public CameraSourceFrameRateTracker(TimeSpan measurementWindow, long timestampFrequency)
    {
        if (measurementWindow <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(measurementWindow));
        }

        if (timestampFrequency <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(timestampFrequency));
        }

        this.timestampFrequency = timestampFrequency;
        measurementWindowTicks = Math.Max(
            1,
            (long)Math.Round(measurementWindow.TotalSeconds * timestampFrequency));
    }

    public CameraSourceFrameRateMeasurement? Observe(long timestamp)
    {
        if (windowStartedAt == long.MinValue)
        {
            windowStartedAt = timestamp;
            previousFrameAt = timestamp;
            frameCount = 1;
            return null;
        }

        if (timestamp < previousFrameAt)
        {
            Reset(timestamp);
            return null;
        }

        maximumGapTicks = Math.Max(maximumGapTicks, timestamp - previousFrameAt);
        previousFrameAt = timestamp;
        frameCount++;
        var elapsedTicks = timestamp - windowStartedAt;
        if (elapsedTicks < measurementWindowTicks || frameCount < 2)
        {
            return null;
        }

        var elapsedSeconds = elapsedTicks / (double)timestampFrequency;
        var measurement = new CameraSourceFrameRateMeasurement(
            (frameCount - 1) / elapsedSeconds,
            frameCount,
            elapsedSeconds,
            maximumGapTicks * 1000d / timestampFrequency);
        Reset(timestamp);
        return measurement;
    }

    private void Reset(long timestamp)
    {
        windowStartedAt = timestamp;
        previousFrameAt = timestamp;
        frameCount = 1;
        maximumGapTicks = 0;
    }
}

internal sealed record CameraSourceFrameRateMeasurement(
    double FramesPerSecond,
    int FrameCount,
    double ElapsedSeconds,
    double MaximumFrameGapMilliseconds);
