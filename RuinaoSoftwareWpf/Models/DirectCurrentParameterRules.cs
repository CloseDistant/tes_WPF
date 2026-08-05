namespace RuinaoSoftwareWpf;

using System.Globalization;

/// <summary>
/// tDCS 参数的唯一范围、步进和格式规则。
/// UI、处方和刺激启动校验必须共同使用本规则，避免显示值与下发值不一致。
/// </summary>
public static class DirectCurrentParameterRules
{
    public const decimal MinimumCurrentMilliamp = 0.01m;
    public const decimal MaximumCurrentMilliamp = 15.00m;
    public const decimal MaximumTimeSeconds = 3600.0m;

    public const string DefaultCurrentMilliamp = "0.01";
    public const string DefaultRampUpSeconds = "0.5";
    public const string DefaultRampDownSeconds = "0.5";
    public const string DefaultTotalDurationSeconds = "1200.0";
    public const string DefaultIntervalSeconds = "0.0";
    public const string DefaultSingleDurationSeconds = "60.0";

    public static DirectCurrentParameterNormalization Normalize(
        DirectCurrentParameterKind kind,
        string? text,
        string fallbackValue)
    {
        var specification = GetSpecification(kind);
        if (!TryParseDecimal(text, out var parsed)
            || parsed < specification.Minimum
            || (!specification.IsMinimumInclusive && parsed == specification.Minimum))
        {
            return new DirectCurrentParameterNormalization(
                false,
                fallbackValue,
                specification.ErrorMessage);
        }

        if (parsed > specification.Maximum)
        {
            return new DirectCurrentParameterNormalization(
                false,
                Format(specification.Maximum, specification.DecimalPlaces),
                specification.RangeAdjustedMessage);
        }

        var rounded = Math.Round(
            parsed,
            specification.DecimalPlaces,
            MidpointRounding.AwayFromZero);
        return new DirectCurrentParameterNormalization(
            true,
            Format(rounded, specification.DecimalPlaces),
            string.Empty);
    }

    public static bool TryParseValidated(
        DirectCurrentParameterKind kind,
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

    public static string FormatTime(double value) =>
        value.ToString("0.0", CultureInfo.InvariantCulture);

    private static DirectCurrentParameterSpecification GetSpecification(DirectCurrentParameterKind kind)
    {
        return kind switch
        {
            DirectCurrentParameterKind.CurrentMilliamp => new(
                MinimumCurrentMilliamp,
                MaximumCurrentMilliamp,
                true,
                2,
                "幅值最小设置步进为 0.01 mA，请输入 0.01～15.00 mA。",
                "幅值允许范围为 0.01～15.00 mA，已调整为 15.00 mA。"),
            DirectCurrentParameterKind.RampUpSeconds => CreateNonNegativeTimeSpecification("渐升时间"),
            DirectCurrentParameterKind.RampDownSeconds => CreateNonNegativeTimeSpecification("渐降时间"),
            DirectCurrentParameterKind.TotalDurationSeconds => CreatePositiveTimeSpecification("刺激时间"),
            DirectCurrentParameterKind.IntervalSeconds => CreateNonNegativeTimeSpecification("间隔时间"),
            DirectCurrentParameterKind.SingleDurationSeconds => CreatePositiveTimeSpecification("单次时长"),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "未知 tDCS 参数。")
        };
    }

    private static DirectCurrentParameterSpecification CreateNonNegativeTimeSpecification(string name) =>
        new(
            0m,
            MaximumTimeSeconds,
            true,
            1,
            $"{name}请输入 0.0～3600.0 s，最小设置步进为 0.1 s。",
            $"{name}允许范围为 0.0～3600.0 s，已调整为 3600.0 s。");

    private static DirectCurrentParameterSpecification CreatePositiveTimeSpecification(string name) =>
        new(
            0.1m,
            MaximumTimeSeconds,
            true,
            1,
            $"{name}请输入 0.1～3600.0 s，最小设置步进为 0.1 s。",
            $"{name}允许范围为 0.1～3600.0 s，已调整为 3600.0 s。");

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
        value.ToString(decimalPlaces == 2 ? "0.00" : "0.0", CultureInfo.InvariantCulture);

    private sealed record DirectCurrentParameterSpecification(
        decimal Minimum,
        decimal Maximum,
        bool IsMinimumInclusive,
        int DecimalPlaces,
        string ErrorMessage,
        string RangeAdjustedMessage);
}

public enum DirectCurrentParameterKind
{
    CurrentMilliamp,
    RampUpSeconds,
    RampDownSeconds,
    TotalDurationSeconds,
    IntervalSeconds,
    SingleDurationSeconds
}

public sealed record DirectCurrentParameterNormalization(
    bool IsValid,
    string Value,
    string ErrorMessage);
