namespace RuinaoSoftwareWpf;

/// <summary>单个tACS通道开始时冻结的独立参数快照。</summary>
public sealed record TacsParameters(
    decimal PeakCurrentMilliampere,
    decimal RampUpSeconds,
    decimal RampDownSeconds,
    uint FrequencyHz,
    decimal TotalDurationSeconds)
{
    public static bool TryCreate(
        ChannelConfig channel,
        out TacsParameters? parameters,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(channel);
        parameters = null;
        if (!TryParameter(TacsParameterKind.PeakCurrentMilliampere, channel.CurrentMA, channel.Name, out var current, out error)
            || !TryParameter(TacsParameterKind.RampUpSeconds, channel.RampUpS, channel.Name, out var rampUp, out error)
            || !TryParameter(TacsParameterKind.RampDownSeconds, channel.RampDownS, channel.Name, out var rampDown, out error)
            || !TryParameter(TacsParameterKind.FrequencyHz, channel.FrequencyHz, channel.Name, out var frequency, out error)
            || !TryParameter(TacsParameterKind.TotalDurationSeconds, channel.DurationS, channel.Name, out var duration, out error))
        {
            return false;
        }

        if (rampUp + rampDown > duration)
        {
            error = $"{channel.Name}：刺激总时间不能小于渐升时间与渐降时间之和。";
            return false;
        }

        parameters = new(current, rampUp, rampDown, decimal.ToUInt32(frequency), duration);
        error = string.Empty;
        return true;
    }

    private static bool TryParameter(
        TacsParameterKind kind,
        string? text,
        string channelName,
        out decimal value,
        out string error)
    {
        if (TacsParameterRules.TryParseValidated(kind, text, out value, out var parameterError))
        {
            error = string.Empty;
            return true;
        }

        error = $"{channelName}：{parameterError}";
        return false;
    }
}
