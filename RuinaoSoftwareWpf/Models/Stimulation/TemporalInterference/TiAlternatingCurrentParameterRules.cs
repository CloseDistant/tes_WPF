using System.Globalization;

namespace RuinaoSoftwareWpf;

public enum TiAlternatingCurrentParameterKind
{
    PeakCurrentMilliampere,
    RampUpSeconds,
    RampDownSeconds,
    FrequencyHz,
    TotalDurationSeconds,
}

public sealed record TiAlternatingCurrentParameterNormalization(
    string Value,
    bool IsValid,
    string ErrorMessage);

/// <summary>TI正式页面的单路交流参数输入规则，与共享硬件DLL冻结规格保持一致。</summary>
public static class TiAlternatingCurrentParameterRules
{
    public const string DefaultPeakCurrentMilliampere = "0.010";
    public const string DefaultRampUpSeconds = "0.5";
    public const string DefaultRampDownSeconds = "0.5";
    public const string DefaultFrequencyHz = "1000";
    public const string DefaultTotalDurationSeconds = "1200.0";

    public static TiAlternatingCurrentParameterNormalization Normalize(
        TiAlternatingCurrentParameterKind kind,
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
        return new(
            Format(rounded, specification.DecimalPlaces),
            true,
            string.Empty);
    }

    public static bool TryParseValidated(
        TiAlternatingCurrentParameterKind kind,
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

    public static string GetDefault(TiAlternatingCurrentParameterKind kind) =>
        kind switch
        {
            TiAlternatingCurrentParameterKind.PeakCurrentMilliampere => DefaultPeakCurrentMilliampere,
            TiAlternatingCurrentParameterKind.RampUpSeconds => DefaultRampUpSeconds,
            TiAlternatingCurrentParameterKind.RampDownSeconds => DefaultRampDownSeconds,
            TiAlternatingCurrentParameterKind.FrequencyHz => DefaultFrequencyHz,
            TiAlternatingCurrentParameterKind.TotalDurationSeconds => DefaultTotalDurationSeconds,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "未知TI交流参数。"),
        };

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

    private static ParameterSpecification GetSpecification(TiAlternatingCurrentParameterKind kind) =>
        kind switch
        {
            TiAlternatingCurrentParameterKind.PeakCurrentMilliampere => new(
                0.001m,
                2.000m,
                3,
                "幅值请输入0.001～2.000mA，最小设置步进为0.001mA。",
                "幅值允许范围为0.001～2.000mA，已调整为2.000mA。"),
            TiAlternatingCurrentParameterKind.RampUpSeconds => CreateRampSpecification("渐升时间"),
            TiAlternatingCurrentParameterKind.RampDownSeconds => CreateRampSpecification("渐降时间"),
            TiAlternatingCurrentParameterKind.FrequencyHz => new(
                1m,
                10_000m,
                0,
                "载波频率请输入1～10000Hz，最小设置步进为1Hz。",
                "载波频率允许范围为1～10000Hz，已调整为10000Hz。"),
            TiAlternatingCurrentParameterKind.TotalDurationSeconds => new(
                0.1m,
                3_600.0m,
                1,
                "刺激总时间请输入0.1～3600.0s，最小设置步进为0.1s。",
                "刺激总时间允许范围为0.1～3600.0s，已调整为3600.0s。"),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "未知TI交流参数。"),
        };

    private static ParameterSpecification CreateRampSpecification(string name) =>
        new(
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
