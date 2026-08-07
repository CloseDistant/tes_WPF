namespace RuinaoTesHardware;

public sealed record MonophasicPulseCurrentStimulationProgress(
    decimal ExpectedCurrentMilliampere,
    TimeSpan Remaining,
    int CompletedPulseCount,
    bool IsCompleted);

/// <summary>
/// 根据已下发的M-tPCS参数计算软件侧预计进度。
/// 结果用于工程诊断，不是硬件状态回读，也不是示波器实测结果。
/// </summary>
public static class MonophasicPulseCurrentStimulationTimeline
{
    public static MonophasicPulseCurrentStimulationProgress Calculate(
        MonophasicPulseCurrentStimulationPlan plan,
        TimeSpan elapsed)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var elapsedSeconds = Math.Max(0m, (decimal)elapsed.TotalSeconds);
        var totalSeconds = plan.Parameters.TotalDurationSeconds;
        var remainingSeconds = Math.Max(0m, totalSeconds - elapsedSeconds);
        if (elapsedSeconds >= totalSeconds)
        {
            return new MonophasicPulseCurrentStimulationProgress(
                0m,
                TimeSpan.Zero,
                plan.PlannedPulseCount,
                IsCompleted: true);
        }

        if (elapsedSeconds >= plan.ScheduledWaveformDurationSeconds)
        {
            return CreateProgress(
                0m,
                remainingSeconds,
                plan.PlannedPulseCount);
        }

        var completedCycles = decimal.ToInt32(decimal.Floor(
            elapsedSeconds / plan.CycleDurationSeconds));
        var cycleSeconds = elapsedSeconds % plan.CycleDurationSeconds;
        var completedPulseCount = completedCycles;
        decimal current;
        if (cycleSeconds >= plan.SinglePulseDurationSeconds)
        {
            current = 0m;
            completedPulseCount++;
        }
        else if (cycleSeconds <= plan.Parameters.RampUpDownSeconds)
        {
            current = plan.Parameters.CurrentMilliampere
                * cycleSeconds
                / plan.Parameters.RampUpDownSeconds;
        }
        else
        {
            var fallElapsed = cycleSeconds - plan.Parameters.RampUpDownSeconds;
            current = plan.Parameters.CurrentMilliampere
                * Math.Max(0m, 1m - fallElapsed / plan.Parameters.RampUpDownSeconds);
        }

        return CreateProgress(
            current,
            remainingSeconds,
            Math.Min(completedPulseCount, plan.PlannedPulseCount));
    }

    private static MonophasicPulseCurrentStimulationProgress CreateProgress(
        decimal currentMilliampere,
        decimal remainingSeconds,
        int completedPulseCount) =>
        new(
            decimal.Round(currentMilliampere, 3, MidpointRounding.AwayFromZero),
            TimeSpan.FromSeconds((double)remainingSeconds),
            completedPulseCount,
            IsCompleted: false);
}
