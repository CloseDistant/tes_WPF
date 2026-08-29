namespace RuinaoSoftwareWpf;

/// <summary>tPCS 参数波形的确定性计算规则。</summary>
public static class PulseCurrentWaveformMath
{
    public static double GetSimulatedCurrent(PulseCurrentParameters parameters, double seconds)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        if (seconds < 0 || seconds > parameters.TotalRuntimeSeconds)
        {
            return 0;
        }

        var riseSeconds = parameters.RiseWidthMilliseconds / 1000d;
        var pulseSeconds = parameters.PulseWidthMilliseconds / 1000d;
        var intervalSeconds = parameters.IntervalWidthMilliseconds / 1000d;
        var cycleSeconds = pulseSeconds + intervalSeconds;
        if (pulseSeconds <= 0 || cycleSeconds <= 0)
        {
            return 0;
        }

        var signedCurrent = string.Equals(parameters.Polarity, PulseCurrentPolarities.Reversed, StringComparison.Ordinal)
            ? -parameters.CurrentMilliamp
            : parameters.CurrentMilliamp;
        if (riseSeconds > 0 && seconds < riseSeconds)
        {
            return signedCurrent * seconds / riseSeconds;
        }

        var treatmentElapsed = seconds - riseSeconds;
        if (treatmentElapsed < 0)
        {
            return 0;
        }

        var pulseIndex = (long)Math.Floor(treatmentElapsed / cycleSeconds);
        if (pulseIndex >= parameters.PlannedTotalCount)
        {
            return 0;
        }

        return treatmentElapsed % cycleSeconds < pulseSeconds ? signedCurrent : 0;
    }

    public static long GetCompletedPulseCount(PulseCurrentParameters parameters, double elapsedSeconds)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        var riseSeconds = parameters.RiseWidthMilliseconds / 1000d;
        var pulseSeconds = parameters.PulseWidthMilliseconds / 1000d;
        var intervalSeconds = parameters.IntervalWidthMilliseconds / 1000d;
        var firstPulseEnd = riseSeconds + pulseSeconds;
        var cycleSeconds = pulseSeconds + intervalSeconds;
        if (elapsedSeconds < firstPulseEnd || cycleSeconds <= 0)
        {
            return 0;
        }

        var completed = (long)Math.Floor((elapsedSeconds - firstPulseEnd) / cycleSeconds) + 1;

        return Math.Clamp(completed, 0, parameters.PlannedTotalCount);
    }
}
