using System.Globalization;

namespace RuinaoTesHardware;

public enum AlternatingCurrentParameterKind
{
    PeakCurrentMilliampere,
    RampUpSeconds,
    RampDownSeconds,
    FrequencyHz,
    TotalDurationSeconds,
}
public sealed record AlternatingCurrentParameterNormalization(
    string Value,
    bool Adjusted,
    string? Message);

/// <summary>
/// tACS工程师软件和后续正式软件共用的单字段参数规则。
/// 组合关系仍由<see cref="AlternatingCurrentStimulationClient.CreatePlan"/>严格校验。
/// </summary>
public static class AlternatingCurrentParameterRules
{
    public const decimal MinimumPeakCurrentMilliampere = 0.001m;
    public const decimal MaximumPeakCurrentMilliampere = 2.000m;
    public const decimal MinimumRampSeconds = 0.0m;
    public const decimal MaximumRampSeconds = 3_600.0m;
    public const decimal MinimumTotalDurationSeconds = 0.1m;
    public const decimal MaximumTotalDurationSeconds = 3_600.0m;
    public const decimal MinimumFrequencyHz = 1m;
    public const decimal MaximumFrequencyHz = 10_000m;

    public const string DefaultPeakCurrentMilliampere = "0.010";
    public const string DefaultRampUpSeconds = "0.5";
    public const string DefaultRampDownSeconds = "0.5";
    public const string DefaultFrequencyHz = "1000";
    public const string DefaultTotalDurationSeconds = "1200.0";

    public static AlternatingCurrentParameterNormalization Normalize(
        AlternatingCurrentParameterKind kind,
        string? text,
        string fallbackValue)
    {
        var specification = GetSpecification(kind);
        if (!TryParseDecimal(text, out var value) || value < specification.Minimum)
        {
            return new AlternatingCurrentParameterNormalization(
                NormalizeFallback(specification, fallbackValue),
                Adjusted: true,
                specification.ErrorMessage);
        }

        if (value > specification.Maximum)
        {
            return new AlternatingCurrentParameterNormalization(
                Format(specification.Maximum, specification.DecimalPlaces),
                Adjusted: true,
                specification.MaximumAdjustedMessage);
        }

        var rounded = decimal.Round(
            value,
            specification.DecimalPlaces,
            MidpointRounding.AwayFromZero);
        return new AlternatingCurrentParameterNormalization(
            Format(rounded, specification.DecimalPlaces),
            Adjusted: rounded != value,
            Message: null);
    }

    public static bool TryValidate(
        AlternatingCurrentParameterKind kind,
        string? text,
        out decimal value,
        out string error)
    {
        var specification = GetSpecification(kind);
        if (!TryParseDecimal(text, out value)
            || value < specification.Minimum
            || value > specification.Maximum
            || decimal.Round(value, specification.DecimalPlaces) != value)
        {
            error = specification.ErrorMessage;
            return false;
        }

        error = string.Empty;
        return true;
    }

    public static string GetDefault(AlternatingCurrentParameterKind kind) =>
        kind switch
        {
            AlternatingCurrentParameterKind.PeakCurrentMilliampere => DefaultPeakCurrentMilliampere,
            AlternatingCurrentParameterKind.RampUpSeconds => DefaultRampUpSeconds,
            AlternatingCurrentParameterKind.RampDownSeconds => DefaultRampDownSeconds,
            AlternatingCurrentParameterKind.FrequencyHz => DefaultFrequencyHz,
            AlternatingCurrentParameterKind.TotalDurationSeconds => DefaultTotalDurationSeconds,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "未知tACS参数。"),
        };

    private static string NormalizeFallback(ParameterSpecification specification, string fallbackValue)
    {
        if (TryParseDecimal(fallbackValue, out var fallback)
            && fallback >= specification.Minimum
            && fallback <= specification.Maximum)
        {
            return Format(
                decimal.Round(fallback, specification.DecimalPlaces, MidpointRounding.AwayFromZero),
                specification.DecimalPlaces);
        }

        return Format(specification.Minimum, specification.DecimalPlaces);
    }

    private static ParameterSpecification GetSpecification(AlternatingCurrentParameterKind kind) =>
        kind switch
        {
            AlternatingCurrentParameterKind.PeakCurrentMilliampere => new(
                MinimumPeakCurrentMilliampere,
                MaximumPeakCurrentMilliampere,
                3,
                "幅值请输入0.001～2.000mA，最小设置步进为0.001mA。",
                "幅值允许范围为0.001～2.000mA，已调整为2.000mA。"),
            AlternatingCurrentParameterKind.RampUpSeconds => CreateRampSpecification("渐升时间"),
            AlternatingCurrentParameterKind.RampDownSeconds => CreateRampSpecification("渐降时间"),
            AlternatingCurrentParameterKind.FrequencyHz => new(
                MinimumFrequencyHz,
                MaximumFrequencyHz,
                0,
                "载波频率请输入1～10000Hz，最小设置步进为1Hz。",
                "载波频率允许范围为1～10000Hz，已调整为10000Hz。"),
            AlternatingCurrentParameterKind.TotalDurationSeconds => new(
                MinimumTotalDurationSeconds,
                MaximumTotalDurationSeconds,
                1,
                "刺激总时间请输入0.1～3600.0s，最小设置步进为0.1s。",
                "刺激总时间允许范围为0.1～3600.0s，已调整为3600.0s。"),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "未知tACS参数。"),
        };

    private static ParameterSpecification CreateRampSpecification(string name) =>
        new(
            MinimumRampSeconds,
            MaximumRampSeconds,
            1,
            $"{name}请输入0.0～3600.0s，最小设置步进为0.1s。",
            $"{name}允许范围为0.0～3600.0s，已调整为3600.0s。");

    private static bool TryParseDecimal(string? text, out decimal value) =>
        decimal.TryParse(
            text?.Trim(),
            NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture,
            out value)
        || decimal.TryParse(
            text?.Trim(),
            NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
            CultureInfo.CurrentCulture,
            out value);

    private static string Format(decimal value, int decimalPlaces) =>
        value.ToString($"F{decimalPlaces}", CultureInfo.InvariantCulture);

    private sealed record ParameterSpecification(
        decimal Minimum,
        decimal Maximum,
        int DecimalPlaces,
        string ErrorMessage,
        string MaximumAdjustedMessage);
}
