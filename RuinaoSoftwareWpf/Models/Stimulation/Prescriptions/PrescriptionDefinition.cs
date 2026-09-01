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
    double? PulseTreatmentDurationSecondsValue = null,
    double? TacsPeakCurrentMilliampereValue = null,
    double? TacsRampUpSecondsValue = null,
    double? TacsRampDownSecondsValue = null,
    int? TacsFrequencyHzValue = null,
    double? TacsTotalDurationSecondsValue = null,
    int? TacsParameterVersion = null)
{
    public const string PulseCurrentStimulationType = StimulationModeCodes.PulseCurrent;

    public bool IsContinuous => DeliveryMode == PrescriptionDeliveryModes.Continuous;
    public bool IsPulseCurrent => string.Equals(StimulationType, PulseCurrentStimulationType, StringComparison.Ordinal);
    public bool IsMonophasicPulseCurrent => string.Equals(
        StimulationType,
        StimulationModeCodes.MonophasicPulseCurrent,
        StringComparison.Ordinal);
    public bool IsTemporalInterference => string.Equals(
        StimulationType,
        StimulationModeCodes.TemporalInterference,
        StringComparison.Ordinal);
    public bool IsTacs => string.Equals(
        StimulationType,
        StimulationModeCodes.AlternatingCurrent,
        StringComparison.Ordinal);
    public string CurrentDisplay => IsTemporalInterference || IsTacs
        ? $"{CurrentMilliamp:0.000} mA"
        : $"{CurrentMilliamp:0.##} mA";
    public string CurrentLabel => "幅值";
    public string TotalDurationLabel => IsPulseCurrent
        ? "治疗时间"
        : IsMonophasicPulseCurrent || IsTemporalInterference || IsTacs ? "刺激时间" : "总时长";
    public string IntervalLabel => IsPulseCurrent ? "间隔宽度" : "间隔时间";
    public string SessionDurationLabel => IsPulseCurrent ? "脉冲宽度" : "单次时长";
    public string RampUpLabel => IsPulseCurrent
        ? "上升宽度"
        : IsMonophasicPulseCurrent ? "渐升时间（渐降同值）" : "渐升时间";
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
    public double TacsPeakCurrentMilliampere =>
        TacsPeakCurrentMilliampereValue ?? CurrentMilliamp;
    public double TacsRampUpSeconds =>
        TacsRampUpSecondsValue ?? DirectCurrentRampUpDurationSeconds;
    public double TacsRampDownSeconds =>
        TacsRampDownSecondsValue ?? DirectCurrentRampDownDurationSeconds;
    public int TacsFrequencyHz => TacsFrequencyHzValue ?? 1000;
    public double TacsTotalDurationSeconds =>
        TacsTotalDurationSecondsValue ?? DirectCurrentTotalDurationSeconds;
    public string TotalDurationDisplay => IsPulseCurrent
        ? $"{PulseCurrentParameterRules.FormatTreatmentDuration(PulseTreatmentDurationSecondsResolved)} s"
        : IsTacs
            ? $"{TacsParameterRules.Format(TacsParameterKind.TotalDurationSeconds, (decimal)TacsTotalDurationSeconds)} s"
            : $"{DirectCurrentParameterRules.FormatTime(DirectCurrentTotalDurationSeconds)} s";
    public string IntervalDisplay => IsPulseCurrent
        ? FormatPulseValue(PulseIntervalWidthMilliseconds, "ms")
        : IsTemporalInterference || IsTacs
            ? "-"
            : IsContinuous ? "/" : $"{DirectCurrentParameterRules.FormatTime(DirectCurrentIntervalDurationSeconds)} s";
    public string SessionDurationDisplay => IsPulseCurrent
        ? FormatPulseValue(PulseWidthMilliseconds, "ms")
        : IsTemporalInterference || IsTacs
            ? "-"
            : IsMonophasicPulseCurrent || IsContinuous
            ? "/"
            : $"{DirectCurrentParameterRules.FormatTime(DirectCurrentSingleDurationSeconds)} s";
    public string RampUpDisplay => IsPulseCurrent
        ? FormatPulseValue(PulseRiseWidthMilliseconds, "ms")
        : IsTacs
            ? $"{TacsParameterRules.Format(TacsParameterKind.RampUpSeconds, (decimal)TacsRampUpSeconds)} s"
            : $"{DirectCurrentParameterRules.FormatTime(DirectCurrentRampUpDurationSeconds)} s";
    public string RampDownDisplay => IsPulseCurrent || IsMonophasicPulseCurrent
        ? "/"
        : IsTacs
            ? $"{TacsParameterRules.Format(TacsParameterKind.RampDownSeconds, (decimal)TacsRampDownSeconds)} s"
            : $"{DirectCurrentParameterRules.FormatTime(DirectCurrentRampDownDurationSeconds)} s";
    public string FrequencyDisplay => IsTacs ? $"{TacsFrequencyHz} Hz" : string.Empty;
    public string DisplayName => string.IsNullOrWhiteSpace(StimulationType)
        ? Name
        : $"{Name} ({StimulationType})";
    public bool HasPulseCurrentParameters =>
        (PulseTreatmentDurationSecondsValue.HasValue || PulseTreatmentDurationSeconds.HasValue)
        && PulseWidthMilliseconds.HasValue
        && PulseRiseWidthMilliseconds.HasValue
        && PulseIntervalWidthMilliseconds.HasValue;
    public string DeliveryModeRowHeight => IsMonophasicPulseCurrent ? "0" : "42";
    public string IntervalRowHeight => "42";
    public string SingleDurationRowHeight => IsMonophasicPulseCurrent ? "0" : "42";
    public string RampDownRowHeight => IsMonophasicPulseCurrent ? "0" : "42";
    public string FrequencyRowHeight => IsTacs ? "42" : "0";

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
