namespace RuinaoSoftwareWpf;

/// <summary>TI单路交流刺激启动时的不可变参数快照。</summary>
public sealed record TiAlternatingCurrentParameters(
    decimal PeakCurrentMilliampere,
    decimal RampUpSeconds,
    decimal RampDownSeconds,
    uint FrequencyHz,
    decimal TotalDurationSeconds)
{
    public static bool TryCreate(
        ChannelConfig channel,
        out TiAlternatingCurrentParameters? parameters,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(channel);
        parameters = null;
        if (!TryParameter(
                TiAlternatingCurrentParameterKind.PeakCurrentMilliampere,
                channel.CurrentMA,
                channel.Name,
                out var peakCurrent,
                out error)
            || !TryParameter(
                TiAlternatingCurrentParameterKind.RampUpSeconds,
                channel.RampUpS,
                channel.Name,
                out var rampUp,
                out error)
            || !TryParameter(
                TiAlternatingCurrentParameterKind.RampDownSeconds,
                channel.RampDownS,
                channel.Name,
                out var rampDown,
                out error)
            || !TryParameter(
                TiAlternatingCurrentParameterKind.FrequencyHz,
                channel.FrequencyHz,
                channel.Name,
                out var frequency,
                out error)
            || !TryParameter(
                TiAlternatingCurrentParameterKind.TotalDurationSeconds,
                channel.DurationS,
                channel.Name,
                out var totalDuration,
                out error))
        {
            return false;
        }

        if (rampUp + rampDown > totalDuration)
        {
            error = $"{channel.Name}：刺激总时间不能小于渐升时间与渐降时间之和。";
            return false;
        }

        parameters = new(
            peakCurrent,
            rampUp,
            rampDown,
            decimal.ToUInt32(frequency),
            totalDuration);
        error = string.Empty;
        return true;
    }

    private static bool TryParameter(
        TiAlternatingCurrentParameterKind kind,
        string? text,
        string channelName,
        out decimal value,
        out string error)
    {
        if (TiAlternatingCurrentParameterRules.TryParseValidated(kind, text, out value, out var parameterError))
        {
            error = string.Empty;
            return true;
        }

        error = $"{channelName}：{parameterError}";
        return false;
    }
}
