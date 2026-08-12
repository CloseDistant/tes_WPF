namespace RuinaoSoftwareWpf;

using System.Globalization;

/// <summary>
/// tPCS 参数在刺激页面、处方编辑和启动校验之间共享的范围、精度与格式规则。
/// </summary>
public static class PulseCurrentParameterRules
{
    public const decimal MinimumCurrentMilliamp = 0.01m;
    public const decimal MaximumCurrentMilliamp = 15.00m;
    public const int MaximumPulseWidthMilliseconds = 2000;
    public const int MaximumRiseWidthMilliseconds = 1000;
    public const int MaximumIntervalWidthMilliseconds = 10000;
    public const decimal MaximumTreatmentDurationSeconds = 3600.0m;

    public const string DefaultCurrentMilliamp = "0.01";
    public const string DefaultPulseWidthMilliseconds = "10";
    public const string DefaultRiseWidthMilliseconds = "5";
    public const string DefaultIntervalWidthMilliseconds = "20";
    public const string DefaultTreatmentDurationSeconds = "1200.0";

    public static PulseCurrentParameterNormalization Normalize(
        PulseCurrentParameterKind kind,
        string? text,
        string fallbackValue)
    {
        var specification = GetSpecification(kind);
        if (!TryParseDecimal(text, out var parsed)
            || parsed < specification.Minimum
            || (!specification.IsMinimumInclusive && parsed == specification.Minimum))
        {
            return new PulseCurrentParameterNormalization(
                false,
                fallbackValue,
                specification.ErrorMessage);
        }

        if (parsed > specification.Maximum)
        {
            return new PulseCurrentParameterNormalization(
                false,
                Format(specification.Maximum, specification.DecimalPlaces),
                specification.RangeAdjustedMessage);
        }

        var rounded = Math.Round(
            parsed,
            specification.DecimalPlaces,
            MidpointRounding.AwayFromZero);
        return new PulseCurrentParameterNormalization(
            true,
            Format(rounded, specification.DecimalPlaces),
            string.Empty);
    }

    public static bool TryParseValidated(
        PulseCurrentParameterKind kind,
        string? text,
        out double value,
        out string error)
    {
        value = 0;
        var specification = GetSpecification(kind);
        if (!TryParseDecimal(text, out var parsed)
            || parsed < specification.Minimum
            || (!specification.IsMinimumInclusive && parsed == specification.Minimum)
            || parsed > specification.Maximum
            || decimal.Round(parsed, specification.DecimalPlaces) != parsed)
        {
            error = specification.ErrorMessage;
            return false;
        }

        value = decimal.ToDouble(parsed);
        error = string.Empty;
        return true;
    }

    public static string FormatCurrent(double value) =>
        value.ToString("0.00", CultureInfo.InvariantCulture);

    public static string FormatTreatmentDuration(double value) =>
        value.ToString("0.0", CultureInfo.InvariantCulture);

    public static long CalculatePlannedTotalCount(
        double treatmentDurationSeconds,
        int riseWidthMilliseconds,
        int pulseWidthMilliseconds,
        int intervalWidthMilliseconds)
    {
        if (!double.IsFinite(treatmentDurationSeconds)
            || treatmentDurationSeconds <= 0
            || riseWidthMilliseconds < 0
            || pulseWidthMilliseconds <= 0
            || intervalWidthMilliseconds <= 0)
        {
            return 0;
        }

        var treatmentMilliseconds = (decimal)treatmentDurationSeconds * 1000m;
        var firstPulseEnd = riseWidthMilliseconds + pulseWidthMilliseconds;
        if (treatmentMilliseconds < firstPulseEnd)
        {
            return 0;
        }

        var count = decimal.Floor(
            (treatmentMilliseconds - riseWidthMilliseconds + intervalWidthMilliseconds)
            / (pulseWidthMilliseconds + intervalWidthMilliseconds));
        return count > long.MaxValue ? long.MaxValue : decimal.ToInt64(count);
    }

    private static PulseCurrentParameterSpecification GetSpecification(PulseCurrentParameterKind kind)
    {
        return kind switch
        {
            PulseCurrentParameterKind.CurrentMilliamp => new(
                MinimumCurrentMilliamp,
                MaximumCurrentMilliamp,
                true,
                2,
                "幅值最小设置步进为 0.01 mA，请输入 0.01～15.00 mA。",
                "幅值允许范围为 0.01～15.00 mA，已调整为 15.00 mA。"),
            PulseCurrentParameterKind.PulseWidthMilliseconds => CreatePositiveIntegerSpecification(
                "脉冲宽度",
                MaximumPulseWidthMilliseconds),
            PulseCurrentParameterKind.RiseWidthMilliseconds => new(
                0m,
                MaximumRiseWidthMilliseconds,
                true,
                0,
                "上升宽度请输入 0～1000 ms 的整数。",
                "上升宽度允许范围为 0～1000 ms，已调整为 1000 ms。"),
            PulseCurrentParameterKind.IntervalWidthMilliseconds => CreatePositiveIntegerSpecification(
                "间隔宽度",
                MaximumIntervalWidthMilliseconds),
            PulseCurrentParameterKind.TreatmentDurationSeconds => new(
                0.1m,
                MaximumTreatmentDurationSeconds,
                true,
                1,
                "治疗时间请输入 0.1～3600.0 s，最小设置步进为 0.1 s。",
                "治疗时间允许范围为 0.1～3600.0 s，已调整为 3600.0 s。"),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "未知 tPCS 参数。")
        };
    }

    private static PulseCurrentParameterSpecification CreatePositiveIntegerSpecification(
        string name,
        int maximum) =>
        new(
            1m,
            maximum,
            true,
            0,
            $"{name}请输入 1～{maximum} ms 的整数。",
            $"{name}允许范围为 1～{maximum} ms，已调整为 {maximum} ms。");

    private static bool TryParseDecimal(string? text, out decimal value)
    {
        return decimal.TryParse(
                text,
                NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out value)
            || decimal.TryParse(
                text,
                NumberStyles.AllowDecimalPoint,
                CultureInfo.CurrentCulture,
                out value);
    }

    private static string Format(decimal value, int decimalPlaces) =>
        value.ToString(
            decimalPlaces switch
            {
                2 => "0.00",
                1 => "0.0",
                _ => "0"
            },
            CultureInfo.InvariantCulture);

    private sealed record PulseCurrentParameterSpecification(
        decimal Minimum,
        decimal Maximum,
        bool IsMinimumInclusive,
        int DecimalPlaces,
        string ErrorMessage,
        string RangeAdjustedMessage);
}
