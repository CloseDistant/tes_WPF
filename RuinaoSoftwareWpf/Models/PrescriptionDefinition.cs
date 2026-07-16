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
    IReadOnlyList<string>? ChannelPolarities = null)
{
    public bool IsContinuous => DeliveryMode == PrescriptionDeliveryModes.Continuous;
    public string CurrentDisplay => $"{CurrentMilliamp:0.##} mA";
    public string TotalDurationDisplay => $"{TotalDurationMinutes} min";
    public string IntervalDisplay => IsContinuous ? "/" : $"{IntervalMinutes} min";
    public string SessionDurationDisplay => IsContinuous ? "/" : $"{SessionDurationMinutes} min";
    public string RampDisplay => $"{RampUpSeconds}s / {RampDownSeconds}s";
    public string DisplayName => string.IsNullOrWhiteSpace(StimulationType)
        ? Name
        : $"{Name} ({StimulationType})";

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
}

public static class PrescriptionDeliveryModes
{
    public const string Interval = "间隔";
    public const string Continuous = "连续";
    public static IReadOnlyList<string> All { get; } = [Interval, Continuous];
}
