using System.Globalization;

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
    double PulseWidthMilliseconds,
    double RiseWidthMilliseconds,
    double IntervalWidthMilliseconds,
    int TreatmentDurationSeconds,
    string Polarity,
    long PlannedTotalCount)
{
    public const double MaxCurrentMilliamp = 15;
    public const int MaxPulseWidthMilliseconds = 1000;
    public const int MaxRiseWidthMilliseconds = 1000;
    public const int MaxIntervalWidthMilliseconds = 10000;

    public static bool TryCreate(
        PulseCurrentChannelConfig channel,
        out PulseCurrentParameters? parameters,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(channel);

        if (!TryDouble(channel.CurrentMilliamp, out var current) || current <= 0 || current > MaxCurrentMilliamp)
        {
            return Fail("幅值必须大于 0 且不超过 15 mA。", out parameters, out error);
        }

        if (!TryDouble(channel.PulseWidthMilliseconds, out var pulseWidth)
            || pulseWidth <= 0
            || pulseWidth > MaxPulseWidthMilliseconds)
        {
            return Fail("脉冲宽度必须大于 0 且不超过 1000 ms。", out parameters, out error);
        }

        if (!TryDouble(channel.RiseWidthMilliseconds, out var riseWidth)
            || riseWidth <= 0
            || riseWidth > MaxRiseWidthMilliseconds)
        {
            return Fail("上升宽度必须大于 0 且不超过 1000 ms。", out parameters, out error);
        }

        var activeWidth = pulseWidth + riseWidth;

        if (!TryDouble(channel.IntervalWidthMilliseconds, out var intervalWidth)
            || intervalWidth < 0
            || intervalWidth > MaxIntervalWidthMilliseconds)
        {
            return Fail("间隔宽度必须在 0–10000 ms 范围内。", out parameters, out error);
        }

        if (!int.TryParse(
                channel.TreatmentDurationSeconds,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var treatmentDuration)
            || treatmentDuration <= 0)
        {
            return Fail("治疗时间必须为大于 0 的整数秒。", out parameters, out error);
        }

        if (!PulseCurrentPolarities.All.Contains(channel.Polarity, StringComparer.Ordinal))
        {
            return Fail("极性必须为“不掉转”或“调转”。", out parameters, out error);
        }

        var treatmentMilliseconds = treatmentDuration * 1000d;
        var totalCountValue = Math.Floor(
            (treatmentMilliseconds + intervalWidth) / (activeWidth + intervalWidth));
        if (totalCountValue < 1 || totalCountValue > long.MaxValue)
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
            (long)totalCountValue);
        error = string.Empty;
        return true;
    }

    private static bool TryDouble(string value, out double result)
    {
        return double.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out result)
            && double.IsFinite(result);
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
