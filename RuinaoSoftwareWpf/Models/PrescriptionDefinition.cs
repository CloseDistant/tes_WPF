namespace RuinaoSoftwareWpf;

using System.Text.RegularExpressions;

/// <summary>与患者、登录账号无关的公用电刺激处方模板。</summary>
public sealed record PrescriptionDefinition(
    string Id,
    string Name,
    string Indication,
    string StimulationType,
    double CurrentMilliamp,
    string DeliveryMode,
    int TotalDurationMinutes,
    int? IntervalMinutes,
    int? SessionDurationMinutes,
    string Course,
    int RampUpSeconds,
    int RampDownSeconds,
    string EvidenceGrade,
    bool IsBuiltin,
    IReadOnlyList<string>? ChannelPolarities = null,
    int? PulseTreatmentDurationSeconds = null,
    int? PulseWidthMilliseconds = null,
    int? PulseRiseWidthMilliseconds = null,
    int? PulseIntervalWidthMilliseconds = null,
    double? DirectCurrentTotalDurationSecondsValue = null,
    double? DirectCurrentIntervalSecondsValue = null,
    double? DirectCurrentSingleDurationSecondsValue = null,
    double? DirectCurrentRampUpSecondsValue = null,
    double? DirectCurrentRampDownSecondsValue = null,
    double? PulseTreatmentDurationSecondsValue = null)
{
    public const string PulseCurrentStimulationType = StimulationModeCodes.PulseCurrent;

    public bool IsContinuous => DeliveryMode == PrescriptionDeliveryModes.Continuous;
    public bool IsPulseCurrent => string.Equals(StimulationType, PulseCurrentStimulationType, StringComparison.Ordinal);
    public string CurrentDisplay => $"{CurrentMilliamp:0.##} mA";
    public string CurrentLabel => "幅值";
    public string TotalDurationLabel => IsPulseCurrent ? "治疗时间" : "总时长";
    public string IntervalLabel => IsPulseCurrent ? "间隔宽度" : "间隔时间";
    public string SessionDurationLabel => IsPulseCurrent ? "脉冲宽度" : "单次时长";
    public string RampUpLabel => IsPulseCurrent ? "上升宽度" : "渐升时间";
    public string RampDownLabel => "渐降时间";
    public double DirectCurrentTotalDurationSeconds =>
        DirectCurrentTotalDurationSecondsValue ?? TotalDurationMinutes * 60d;
    public double DirectCurrentIntervalDurationSeconds =>
        IsContinuous ? 0d : DirectCurrentIntervalSecondsValue ?? (IntervalMinutes ?? 0) * 60d;
    public double DirectCurrentSingleDurationSeconds =>
        IsContinuous
            ? 0d
            : DirectCurrentSingleDurationSecondsValue
                ?? (SessionDurationMinutes ?? TotalDurationMinutes) * 60d;
    public double DirectCurrentRampUpDurationSeconds =>
        DirectCurrentRampUpSecondsValue ?? RampUpSeconds;
    public double DirectCurrentRampDownDurationSeconds =>
        DirectCurrentRampDownSecondsValue ?? RampDownSeconds;
    public double PulseTreatmentDurationSecondsResolved =>
        PulseTreatmentDurationSecondsValue ?? PulseTreatmentDurationSeconds ?? 0d;
    public string TotalDurationDisplay => IsPulseCurrent
        ? $"{PulseCurrentParameterRules.FormatTreatmentDuration(PulseTreatmentDurationSecondsResolved)} s"
        : $"{DirectCurrentParameterRules.FormatTime(DirectCurrentTotalDurationSeconds)} s";
    public string IntervalDisplay => IsPulseCurrent
        ? FormatPulseValue(PulseIntervalWidthMilliseconds, "ms")
        : IsContinuous ? "/" : $"{DirectCurrentParameterRules.FormatTime(DirectCurrentIntervalDurationSeconds)} s";
    public string SessionDurationDisplay => IsPulseCurrent
        ? FormatPulseValue(PulseWidthMilliseconds, "ms")
        : IsContinuous ? "/" : $"{DirectCurrentParameterRules.FormatTime(DirectCurrentSingleDurationSeconds)} s";
    public string RampUpDisplay => IsPulseCurrent
        ? FormatPulseValue(PulseRiseWidthMilliseconds, "ms")
        : $"{DirectCurrentParameterRules.FormatTime(DirectCurrentRampUpDurationSeconds)} s";
    public string RampDownDisplay => IsPulseCurrent
        ? "/"
        : $"{DirectCurrentParameterRules.FormatTime(DirectCurrentRampDownDurationSeconds)} s";
    public string DisplayName => string.IsNullOrWhiteSpace(StimulationType)
        ? Name
        : $"{Name} ({StimulationType})";
    public bool HasPulseCurrentParameters =>
        (PulseTreatmentDurationSecondsValue.HasValue || PulseTreatmentDurationSeconds.HasValue)
        && PulseWidthMilliseconds.HasValue
        && PulseRiseWidthMilliseconds.HasValue
        && PulseIntervalWidthMilliseconds.HasValue;

    public string GetChannelPolarity(int channelIndex)
    {
        if (ChannelPolarities is not null
            && channelIndex >= 0
            && channelIndex < ChannelPolarities.Count
            && string.Equals(ChannelPolarities[channelIndex], "调转", StringComparison.Ordinal))
        {
            return "调转";
        }

        return "不掉转";
    }

    public static string NormalizeName(string name, string stimulationType)
    {
        var normalized = name.Trim();
        if (!string.IsNullOrWhiteSpace(stimulationType))
        {
            normalized = Regex.Replace(
                normalized,
                $@"\s*\({Regex.Escape(stimulationType.Trim())}\)\s*",
                " ",
                RegexOptions.IgnoreCase);
        }

        normalized = Regex.Replace(normalized, @"\s+", " ").Trim();
        normalized = Regex.Replace(normalized, @"\s+(\d+)$", "-$1");
        return normalized;
    }

    private static string FormatPulseValue(int? value, string unit) =>
        value.HasValue ? $"{value.Value} {unit}" : string.Empty;
}

public static class PrescriptionDeliveryModes
{
    public const string Interval = "间隔";
    public const string Continuous = "连续";
    public static IReadOnlyList<string> All { get; } = [Interval, Continuous];
}
