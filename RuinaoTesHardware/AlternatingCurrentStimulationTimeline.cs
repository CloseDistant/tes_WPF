namespace RuinaoTesHardware;

/// <summary>
/// 根据已下发tACS计划计算软件侧瞬时正弦值和阶梯包络。
/// 结果仅用于工程预览，不是硬件状态或实测电流。
/// </summary>
public static class AlternatingCurrentStimulationTimeline
{
    public static AlternatingCurrentStimulationProgress Calculate(
        AlternatingCurrentStimulationPlan plan,
        TimeSpan elapsed)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var elapsedSeconds = Math.Max(0m, (decimal)elapsed.TotalSeconds);
        var totalSeconds = plan.Parameters.TotalDurationSeconds;
        if (elapsedSeconds >= totalSeconds)
        {
            return new AlternatingCurrentStimulationProgress(
                0m,
                0m,
                SegmentIndex: 0,
                Stage: null,
                TimeSpan.Zero,
                IsCompleted: true);
        }

        var elapsedMicroseconds = decimal.ToUInt32(decimal.Floor(elapsedSeconds * 1_000_000m));
        var segment = plan.Segments.First(value =>
            elapsedMicroseconds >= value.StartMicroseconds
            && elapsedMicroseconds < value.StartMicroseconds + value.DurationMicroseconds);
        var phaseRadians = 2d
            * Math.PI
            * plan.Parameters.FrequencyHz
            * (double)elapsedSeconds;
        var current = segment.PeakCurrentMilliampere * (decimal)Math.Sin(phaseRadians);
        return new AlternatingCurrentStimulationProgress(
            decimal.Round(current, 3, MidpointRounding.AwayFromZero),
            decimal.Round(segment.PeakCurrentMilliampere, 3, MidpointRounding.AwayFromZero),
            segment.Index,
            segment.Stage,
            TimeSpan.FromSeconds((double)(totalSeconds - elapsedSeconds)),
            IsCompleted: false);
    }
}
