namespace RuinaoTesHardware;

public sealed record PulseCurrentStimulationProgress(
    decimal ExpectedCurrentMilliampere,
    int CompletedPulseCount,
    TimeSpan Remaining,
    bool IsCompleted);

/// <summary>工程师工具使用的软件时间轴，不代表硬件输出测量结果。</summary>
public static class PulseCurrentStimulationTimeline
{
    public static PulseCurrentStimulationProgress Calculate(
        PulseCurrentStimulationPlan plan,
        TimeSpan elapsed)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var elapsedMilliseconds = Math.Max(0m, (decimal)elapsed.TotalMilliseconds);
        var totalMilliseconds = plan.TotalTimeMilliseconds;
        var remainingMilliseconds = Math.Max(0m, totalMilliseconds - elapsedMilliseconds);
        if (elapsedMilliseconds >= totalMilliseconds)
        {
            return new PulseCurrentStimulationProgress(
                ExpectedCurrentMilliampere: 0m,
                plan.PlannedPulseCount,
                TimeSpan.Zero,
                IsCompleted: true);
        }

        var rampMilliseconds = plan.Parameters.RampWidthMilliseconds;
        if (rampMilliseconds > 0m && elapsedMilliseconds < rampMilliseconds)
        {
            return new PulseCurrentStimulationProgress(
                plan.SignedCurrentMilliampere * elapsedMilliseconds / rampMilliseconds,
                CompletedPulseCount: 0,
                TimeSpan.FromMilliseconds((double)remainingMilliseconds),
                IsCompleted: false);
        }

        var treatmentElapsedMilliseconds = Math.Max(0m, elapsedMilliseconds - rampMilliseconds);
        if (treatmentElapsedMilliseconds >= plan.ScheduledPulseDurationMilliseconds)
        {
            return new PulseCurrentStimulationProgress(
                ExpectedCurrentMilliampere: 0m,
                plan.PlannedPulseCount,
                TimeSpan.FromMilliseconds((double)remainingMilliseconds),
                IsCompleted: false);
        }

        var pulseMilliseconds = plan.Parameters.PulseWidthMilliseconds;
        var intervalMilliseconds = plan.Parameters.IntervalWidthMilliseconds;
        var cycleMilliseconds = pulseMilliseconds + intervalMilliseconds;
        var cycleIndex = decimal.ToInt32(decimal.Floor(treatmentElapsedMilliseconds / cycleMilliseconds));
        var cyclePosition = treatmentElapsedMilliseconds - cycleIndex * cycleMilliseconds;
        var completedPulseCount = Math.Min(
            plan.PlannedPulseCount,
            decimal.ToInt32(decimal.Floor(
                (treatmentElapsedMilliseconds + intervalMilliseconds) / cycleMilliseconds)));
        var expectedCurrent = cycleIndex < plan.PlannedPulseCount && cyclePosition < pulseMilliseconds
            ? plan.SignedCurrentMilliampere
            : 0m;
        return new PulseCurrentStimulationProgress(
            expectedCurrent,
            completedPulseCount,
            TimeSpan.FromMilliseconds((double)remainingMilliseconds),
            IsCompleted: false);
    }
}
