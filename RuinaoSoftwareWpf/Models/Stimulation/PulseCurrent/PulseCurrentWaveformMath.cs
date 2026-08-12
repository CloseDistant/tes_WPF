namespace RuinaoSoftwareWpf;

/// <summary>tPCS 参数波形的确定性计算规则。</summary>
public static class PulseCurrentWaveformMath
{
    public static double GetSimulatedCurrent(PulseCurrentParameters parameters, double seconds)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        if (seconds < 0 || seconds > parameters.TreatmentDurationSeconds)
        {
            return 0;
        }

        var riseSeconds = parameters.RiseWidthMilliseconds / 1000d;
        var pulseSeconds = parameters.PulseWidthMilliseconds / 1000d;
        var intervalSeconds = parameters.IntervalWidthMilliseconds / 1000d;
        var firstPulseEnd = riseSeconds + pulseSeconds;
        var subsequentCycleSeconds = pulseSeconds + intervalSeconds;
        if (pulseSeconds <= 0 || subsequentCycleSeconds <= 0)
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

        if (seconds < firstPulseEnd)
        {
            return signedCurrent;
        }

        if (parameters.PlannedTotalCount <= 1 || seconds < firstPulseEnd + intervalSeconds)
        {
            return 0;
        }

        var subsequentElapsed = seconds - firstPulseEnd - intervalSeconds;
        var subsequentPulseIndex = (long)Math.Floor(subsequentElapsed / subsequentCycleSeconds) + 1;
        if (subsequentPulseIndex >= parameters.PlannedTotalCount)
        {
            return 0;
        }

        return subsequentElapsed % subsequentCycleSeconds < pulseSeconds ? signedCurrent : 0;
    }

    public static long GetCompletedPulseCount(PulseCurrentParameters parameters, double elapsedSeconds)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        var riseSeconds = parameters.RiseWidthMilliseconds / 1000d;
        var pulseSeconds = parameters.PulseWidthMilliseconds / 1000d;
        var intervalSeconds = parameters.IntervalWidthMilliseconds / 1000d;
        var firstPulseEnd = riseSeconds + pulseSeconds;
        var subsequentCycleSeconds = pulseSeconds + intervalSeconds;
        if (elapsedSeconds < firstPulseEnd || subsequentCycleSeconds <= 0)
        {
            return 0;
        }

        var completed = 1L;
        var secondPulseEnd = firstPulseEnd + intervalSeconds + pulseSeconds;
        if (elapsedSeconds >= secondPulseEnd)
        {
            completed += (long)Math.Floor((elapsedSeconds - secondPulseEnd) / subsequentCycleSeconds) + 1;
        }

        return Math.Clamp(completed, 0, parameters.PlannedTotalCount);
    }
}
