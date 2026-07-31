namespace RuinaoSoftwareWpf;

public static class PulseCurrentPolarities
{
    public const string NotReversed = "不掉转";
    public const string Reversed = "调转";
    public static IReadOnlyList<string> All { get; } = [NotReversed, Reversed];
}

/// <summary>
/// 已校验的 tPCS 参数快照。最后一次脉冲之后不计算间隔宽度。
/// </summary>
public sealed record PulseCurrentParameters(
    double CurrentMilliamp,
    int PulseWidthMilliseconds,
    int RiseWidthMilliseconds,
    int IntervalWidthMilliseconds,
    double TreatmentDurationSeconds,
    string Polarity,
    long PlannedTotalCount)
{
    public const double MaxCurrentMilliamp = (double)PulseCurrentParameterRules.MaximumCurrentMilliamp;
    public const int MaxPulseWidthMilliseconds = PulseCurrentParameterRules.MaximumPulseWidthMilliseconds;
    public const int MaxRiseWidthMilliseconds = PulseCurrentParameterRules.MaximumRiseWidthMilliseconds;
    public const int MaxIntervalWidthMilliseconds = PulseCurrentParameterRules.MaximumIntervalWidthMilliseconds;

    public static bool TryCreate(
        PulseCurrentChannelConfig channel,
        out PulseCurrentParameters? parameters,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(channel);

        if (!PulseCurrentParameterRules.TryParseValidated(
                PulseCurrentParameterKind.CurrentMilliamp,
                channel.CurrentMilliamp,
                out var current,
                out error))
        {
            parameters = null;
            return false;
        }

        if (!TryValidatedInteger(
                PulseCurrentParameterKind.PulseWidthMilliseconds,
                channel.PulseWidthMilliseconds,
                out var pulseWidth,
                out error))
        {
            parameters = null;
            return false;
        }

        if (!TryValidatedInteger(
                PulseCurrentParameterKind.RiseWidthMilliseconds,
                channel.RiseWidthMilliseconds,
                out var riseWidth,
                out error))
        {
            parameters = null;
            return false;
        }

        if (!TryValidatedInteger(
                PulseCurrentParameterKind.IntervalWidthMilliseconds,
                channel.IntervalWidthMilliseconds,
                out var intervalWidth,
                out error))
        {
            parameters = null;
            return false;
        }

        if (!PulseCurrentParameterRules.TryParseValidated(
                PulseCurrentParameterKind.TreatmentDurationSeconds,
                channel.TreatmentDurationSeconds,
                out var treatmentDuration,
                out error))
        {
            parameters = null;
            return false;
        }

        if (!PulseCurrentPolarities.All.Contains(channel.Polarity, StringComparer.Ordinal))
        {
            return Fail("极性必须为“不掉转”或“调转”。", out parameters, out error);
        }

        var totalCount = PulseCurrentParameterRules.CalculatePlannedTotalCount(
            treatmentDuration,
            riseWidth,
            pulseWidth,
            intervalWidth);
        if (totalCount < 1)
        {
            return Fail("治疗时间不足以完成一次完整脉冲。", out parameters, out error);
        }

        parameters = new PulseCurrentParameters(
            current,
            pulseWidth,
            riseWidth,
            intervalWidth,
            treatmentDuration,
            channel.Polarity,
            totalCount);
        error = string.Empty;
        return true;
    }

    private static bool TryValidatedInteger(
        PulseCurrentParameterKind kind,
        string value,
        out int result,
        out string error)
    {
        result = 0;
        if (!PulseCurrentParameterRules.TryParseValidated(kind, value, out var parsed, out error))
        {
            return false;
        }

        result = checked((int)parsed);
        return true;
    }

    private static bool Fail(
        string message,
        out PulseCurrentParameters? parameters,
        out string error)
    {
        parameters = null;
        error = message;
        return false;
    }
}
