namespace RuinaoTesHardware;

public sealed record DirectCurrentStimulationProgress(
    decimal ExpectedCurrentMilliampere,
    TimeSpan Remaining,
    bool IsCompleted);

/// <summary>
/// 根据已下发的产品tDCS参数计算软件侧预计进度。
/// 结果用于界面预览，不是硬件状态回读，也不能替代外部电流测量。
/// </summary>
public static class DirectCurrentStimulationTimeline
{
    public static DirectCurrentStimulationProgress Calculate(
        DirectCurrentStimulationPlan plan,
        TimeSpan elapsed)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var totalSeconds = plan.Parameters.TotalDurationSeconds;
        var elapsedSeconds = Math.Max(0m, (decimal)elapsed.TotalSeconds);
        var remainingSeconds = Math.Max(0m, totalSeconds - elapsedSeconds);
        if (elapsedSeconds >= totalSeconds)
        {
            return new DirectCurrentStimulationProgress(
                0m,
                TimeSpan.Zero,
                IsCompleted: true);
        }

        var cycleSeconds = elapsedSeconds;
        if (plan.Parameters.DeliveryMode == DirectCurrentDeliveryMode.Intermittent)
        {
            var fullCycleSeconds =
                plan.Parameters.SingleDurationSeconds + plan.Parameters.IntervalSeconds;
            cycleSeconds = fullCycleSeconds > 0m
                ? elapsedSeconds % fullCycleSeconds
                : 0m;
            if (cycleSeconds >= plan.Parameters.SingleDurationSeconds)
            {
                return CreateProgress(0m, remainingSeconds);
            }
        }

        var current = CalculateStimulationSegmentCurrent(plan.Parameters, cycleSeconds);
        if (plan.Parameters.Polarity == DirectCurrentPolarity.Reversed)
        {
            current = -current;
        }

        return CreateProgress(current, remainingSeconds);
    }

    private static decimal CalculateStimulationSegmentCurrent(
        DirectCurrentStimulationParameters parameters,
        decimal elapsedSeconds)
    {
        var segmentDuration = parameters.DeliveryMode == DirectCurrentDeliveryMode.Continuous
            ? parameters.TotalDurationSeconds
            : parameters.SingleDurationSeconds;
        if (parameters.RampUpSeconds > 0m && elapsedSeconds < parameters.RampUpSeconds)
        {
            return parameters.CurrentMilliampere * elapsedSeconds / parameters.RampUpSeconds;
        }

        var rampDownStart = segmentDuration - parameters.RampDownSeconds;
        if (parameters.RampDownSeconds > 0m && elapsedSeconds >= rampDownStart)
        {
            var rampDownElapsed = elapsedSeconds - rampDownStart;
            return parameters.CurrentMilliampere
                * Math.Max(0m, 1m - rampDownElapsed / parameters.RampDownSeconds);
        }

        return parameters.CurrentMilliampere;
    }

    private static DirectCurrentStimulationProgress CreateProgress(
        decimal currentMilliampere,
        decimal remainingSeconds) =>
        new(
            decimal.Round(currentMilliampere, 3, MidpointRounding.AwayFromZero),
            TimeSpan.FromSeconds((double)remainingSeconds),
            IsCompleted: false);
}
