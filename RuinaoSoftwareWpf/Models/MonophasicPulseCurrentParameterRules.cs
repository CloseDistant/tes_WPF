using System.Globalization;

namespace RuinaoSoftwareWpf;

/// <summary>M-tPCS 在刺激页、处方和启动校验之间共享的唯一参数规则。</summary>
public static class MonophasicPulseCurrentParameterRules
{
    public const string DefaultCurrentMilliamp = "0.01";
    public const string DefaultRampSeconds = "0.5";
    public const string DefaultIntervalSeconds = "0.0";
    public const string DefaultTotalDurationSeconds = "120.0";

    public static MonophasicPulseCurrentParameterNormalization Normalize(
        MonophasicPulseCurrentParameterKind kind,
        string text,
        string fallbackValue)
    {
        var (minimum, maximum, decimals) = GetSpecification(kind);
        if (!TryParseFiniteNumber(text, out var value) || value < minimum)
        {
            return new MonophasicPulseCurrentParameterNormalization(
                fallbackValue,
                false,
                GetRangeError(kind, minimum, maximum, decimals));
        }

        if (value > maximum)
        {
            return new MonophasicPulseCurrentParameterNormalization(
                Format(kind, maximum),
                false,
                $"{GetName(kind)}允许范围为 {FormatNumber(minimum, decimals)}～{FormatNumber(maximum, decimals)}，"
                    + $"已调整为 {FormatNumber(maximum, decimals)}。");
        }

        var rounded = Math.Round(value, decimals, MidpointRounding.AwayFromZero);
        return new MonophasicPulseCurrentParameterNormalization(Format(kind, rounded), true, string.Empty);
    }

    public static bool TryParseValidated(
        MonophasicPulseCurrentParameterKind kind,
        string? text,
        out double value,
        out string error)
    {
        value = 0;
        error = string.Empty;
        if (!TryParseFiniteNumber(text, out value))
        {
            error = $"{GetName(kind)}请输入有效数字。";
            return false;
        }

        var (minimum, maximum, decimals) = GetSpecification(kind);
        var rounded = Math.Round(value, decimals, MidpointRounding.AwayFromZero);
        if (value < minimum || value > maximum || Math.Abs(value - rounded) > 0.0000001d)
        {
            error = GetRangeError(kind, minimum, maximum, decimals);
            return false;
        }

        value = rounded;
        return true;
    }

    public static bool TryCreateWaveform(
        ChannelConfig channel,
        out DirectCurrentWaveformParameters? parameters,
        out string error)
    {
        parameters = null;
        if (!TryChannelValue(channel, MonophasicPulseCurrentParameterKind.CurrentMilliamp, channel.CurrentMA, out var current, out error)
            || !TryChannelValue(channel, MonophasicPulseCurrentParameterKind.RampSeconds, channel.RampUpS, out var ramp, out error)
            || !TryChannelValue(channel, MonophasicPulseCurrentParameterKind.IntervalSeconds, channel.IntervalS, out var interval, out error)
            || !TryChannelValue(channel, MonophasicPulseCurrentParameterKind.TotalDurationSeconds, channel.DurationS, out var total, out error))
        {
            return false;
        }

        if (total < ramp * 2d)
        {
            error = $"{channel.Name}：刺激时间不能小于一个完整三角脉冲时长（2×渐升时间）。";
            return false;
        }

        parameters = new DirectCurrentWaveformParameters(
            current,
            ramp,
            ramp,
            total,
            interval,
            PlateauSeconds: 0,
            IsContinuous: false,
            ReversePolarity: false);
        error = string.Empty;
        return true;
    }

    public static string Format(MonophasicPulseCurrentParameterKind kind, double value) =>
        kind == MonophasicPulseCurrentParameterKind.CurrentMilliamp
            ? value.ToString("0.00", CultureInfo.InvariantCulture)
            : value.ToString("0.0", CultureInfo.InvariantCulture);

    public static string GetDefault(MonophasicPulseCurrentParameterKind kind) => kind switch
    {
        MonophasicPulseCurrentParameterKind.CurrentMilliamp => DefaultCurrentMilliamp,
        MonophasicPulseCurrentParameterKind.RampSeconds => DefaultRampSeconds,
        MonophasicPulseCurrentParameterKind.IntervalSeconds => DefaultIntervalSeconds,
        MonophasicPulseCurrentParameterKind.TotalDurationSeconds => DefaultTotalDurationSeconds,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "未知 M-tPCS 参数。")
    };

    private static bool TryChannelValue(
        ChannelConfig channel,
        MonophasicPulseCurrentParameterKind kind,
        string text,
        out double value,
        out string error)
    {
        if (TryParseValidated(kind, text, out value, out error))
        {
            return true;
        }

        error = $"{channel.Name}：{error}";
        return false;
    }

    private static (double Minimum, double Maximum, int Decimals) GetSpecification(
        MonophasicPulseCurrentParameterKind kind) => kind switch
    {
        MonophasicPulseCurrentParameterKind.CurrentMilliamp => (0.01d, 15.00d, 2),
        MonophasicPulseCurrentParameterKind.RampSeconds => (0.1d, 100.0d, 1),
        MonophasicPulseCurrentParameterKind.IntervalSeconds => (0.0d, 3600.0d, 1),
        MonophasicPulseCurrentParameterKind.TotalDurationSeconds => (0.2d, 3600.0d, 1),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "未知 M-tPCS 参数。")
    };

    private static string GetName(MonophasicPulseCurrentParameterKind kind) => kind switch
    {
        MonophasicPulseCurrentParameterKind.CurrentMilliamp => "幅值",
        MonophasicPulseCurrentParameterKind.RampSeconds => "渐升时间（渐降同值）",
        MonophasicPulseCurrentParameterKind.IntervalSeconds => "间隔时间",
        MonophasicPulseCurrentParameterKind.TotalDurationSeconds => "刺激时间",
        _ => "参数"
    };

    private static string FormatNumber(double value, int decimals) =>
        value.ToString(decimals == 2 ? "0.00" : "0.0", CultureInfo.InvariantCulture);

    private static bool TryParseFiniteNumber(string? text, out double value) =>
        (double.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value)
            || double.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out value))
        && !double.IsNaN(value)
        && !double.IsInfinity(value);

    private static string GetRangeError(
        MonophasicPulseCurrentParameterKind kind,
        double minimum,
        double maximum,
        int decimals) =>
        $"{GetName(kind)}范围为 {FormatNumber(minimum, decimals)}～{FormatNumber(maximum, decimals)}，"
            + $"最多保留 {decimals} 位小数。";
}

public enum MonophasicPulseCurrentParameterKind
{
    CurrentMilliamp,
    RampSeconds,
    IntervalSeconds,
    TotalDurationSeconds
}

public sealed record MonophasicPulseCurrentParameterNormalization(
    string Value,
    bool IsValid,
    string ErrorMessage);
