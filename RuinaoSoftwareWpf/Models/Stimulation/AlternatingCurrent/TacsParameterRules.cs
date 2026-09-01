using System.Globalization;

namespace RuinaoSoftwareWpf;

public enum TacsParameterKind
{
    PeakCurrentMilliampere,
    RampUpSeconds,
    RampDownSeconds,
    FrequencyHz,
    TotalDurationSeconds,
}

public sealed record TacsParameterNormalization(
    string Value,
    bool IsValid,
    string ErrorMessage);

/// <summary>
/// tACS参数的独立业务规则。当前规格与TI单路参数一致，但不依赖TI类型，
/// 以便后续两个刺激模式分别演进。
/// </summary>
public static class TacsParameterRules
{
    public const string DefaultPeakCurrentMilliampere = "0.010";
    public const string DefaultRampUpSeconds = "0.5";
    public const string DefaultRampDownSeconds = "0.5";
    public const string DefaultFrequencyHz = "1000";
    public const string DefaultTotalDurationSeconds = "1200.0";

    public static TacsParameterNormalization Normalize(
        TacsParameterKind kind,
        string? text,
        string fallbackValue)
    {
        var specification = GetSpecification(kind);
        if (!TryParse(text, out var value) || value < specification.Minimum)
        {
            return new(
                NormalizeFallback(specification, fallbackValue),
                false,
                specification.ErrorMessage);
        }

        if (value > specification.Maximum)
        {
            return new(
                Format(specification.Maximum, specification.DecimalPlaces),
                false,
                specification.MaximumAdjustedMessage);
        }

        var rounded = decimal.Round(value, specification.DecimalPlaces, MidpointRounding.AwayFromZero);
        return new(Format(rounded, specification.DecimalPlaces), true, string.Empty);
    }

    public static bool TryParseValidated(
        TacsParameterKind kind,
        string? text,
        out decimal value,
        out string error)
    {
        var specification = GetSpecification(kind);
        if (!TryParse(text, out value)
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

    public static string GetDefault(TacsParameterKind kind) => kind switch
    {
        TacsParameterKind.PeakCurrentMilliampere => DefaultPeakCurrentMilliampere,
        TacsParameterKind.RampUpSeconds => DefaultRampUpSeconds,
        TacsParameterKind.RampDownSeconds => DefaultRampDownSeconds,
        TacsParameterKind.FrequencyHz => DefaultFrequencyHz,
        TacsParameterKind.TotalDurationSeconds => DefaultTotalDurationSeconds,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "未知tACS参数。"),
    };

    public static string Format(TacsParameterKind kind, decimal value)
    {
        var specification = GetSpecification(kind);
        return Format(
            decimal.Round(value, specification.DecimalPlaces, MidpointRounding.AwayFromZero),
            specification.DecimalPlaces);
    }

    private static string NormalizeFallback(ParameterSpecification specification, string fallbackValue)
    {
        if (TryParse(fallbackValue, out var fallback)
            && fallback >= specification.Minimum
            && fallback <= specification.Maximum)
        {
            return Format(
                decimal.Round(fallback, specification.DecimalPlaces, MidpointRounding.AwayFromZero),
                specification.DecimalPlaces);
        }

        return Format(specification.Minimum, specification.DecimalPlaces);
    }

    private static ParameterSpecification GetSpecification(TacsParameterKind kind) => kind switch
    {
        TacsParameterKind.PeakCurrentMilliampere => new(
            0.001m,
            2.000m,
            3,
            "幅值请输入0.001～2.000mA，最小设置步进为0.001mA。",
            "幅值允许范围为0.001～2.000mA，已调整为2.000mA。"),
        TacsParameterKind.RampUpSeconds => CreateRampSpecification("渐升时间"),
        TacsParameterKind.RampDownSeconds => CreateRampSpecification("渐降时间"),
        TacsParameterKind.FrequencyHz => new(
            1m,
            10_000m,
            0,
            "载波频率请输入1～10000Hz，最小设置步进为1Hz。",
            "载波频率允许范围为1～10000Hz，已调整为10000Hz。"),
        TacsParameterKind.TotalDurationSeconds => new(
            0.1m,
            3_600.0m,
            1,
            "刺激总时间请输入0.1～3600.0s，最小设置步进为0.1s。",
            "刺激总时间允许范围为0.1～3600.0s，已调整为3600.0s。"),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "未知tACS参数。"),
    };

    private static ParameterSpecification CreateRampSpecification(string name) => new(
        0.0m,
        3_600.0m,
        1,
        $"{name}请输入0.0～3600.0s，最小设置步进为0.1s。",
        $"{name}允许范围为0.0～3600.0s，已调整为3600.0s。");

    private static bool TryParse(string? text, out decimal value) =>
        decimal.TryParse(text?.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out value)
        || decimal.TryParse(text?.Trim(), NumberStyles.Number, CultureInfo.CurrentCulture, out value);

    private static string Format(decimal value, int decimalPlaces) =>
        value.ToString($"F{decimalPlaces}", CultureInfo.InvariantCulture);

    private sealed record ParameterSpecification(
        decimal Minimum,
        decimal Maximum,
        int DecimalPlaces,
        string ErrorMessage,
        string MaximumAdjustedMessage);
}
