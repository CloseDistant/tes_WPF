namespace RuinaoSoftwareWpf;

/// <summary>tDCS 模拟波形的参数快照。波形由参数和时间即时计算，不保存历史采样点。</summary>
public sealed record DirectCurrentWaveformParameters(
    double CurrentMilliamp,
    double RampUpSeconds,
    double RampDownSeconds,
    double TotalDurationSeconds,
    double IntervalSeconds,
    double PlateauSeconds,
    bool IsContinuous,
    bool ReversePolarity)
{
    public static bool TryCreate(ChannelConfig channel, out DirectCurrentWaveformParameters? parameters, out string error)
    {
        parameters = null;
        error = string.Empty;
        if (!TryParameter(DirectCurrentParameterKind.CurrentMilliamp, channel.CurrentMA, channel.Name, out var current, out error)
            || !TryParameter(DirectCurrentParameterKind.RampUpSeconds, channel.RampUpS, channel.Name, out var rampUp, out error)
            || !TryParameter(DirectCurrentParameterKind.RampDownSeconds, channel.RampDownS, channel.Name, out var rampDown, out error)
            || !TryParameter(DirectCurrentParameterKind.TotalDurationSeconds, channel.DurationS, channel.Name, out var totalDuration, out error))
        {
            return false;
        }

        var continuous = channel.IsContinuousMode;
        var interval = 0d;
        var plateau = 0d;
        if (continuous)
        {
            if (rampUp + rampDown > totalDuration)
            {
                error = $"{channel.Name}：刺激时间不能小于渐升与渐降时间之和。";
                return false;
            }
        }
        else
        {
            if (!TryParameter(DirectCurrentParameterKind.IntervalSeconds, channel.IntervalS, channel.Name, out interval, out error)
                || !TryParameter(DirectCurrentParameterKind.SingleDurationSeconds, channel.SingleDurationS, channel.Name, out var singleDuration, out error))
            {
                return false;
            }

            if (rampUp + rampDown >= singleDuration)
            {
                error = $"{channel.Name}：单次时长必须大于渐升时间与渐降时间之和。";
                return false;
            }

            if (rampUp + rampDown > totalDuration)
            {
                error = $"{channel.Name}：刺激时间不足以完成一次渐升和渐降。";
                return false;
            }

            plateau = singleDuration - rampUp - rampDown;
        }

        parameters = new DirectCurrentWaveformParameters(
            current,
            rampUp,
            rampDown,
            totalDuration,
            interval,
            plateau,
            continuous,
            string.Equals(channel.Polarity, "调转", StringComparison.Ordinal));
        return true;
    }

    private static bool TryParameter(
        DirectCurrentParameterKind kind,
        string? text,
        string channelName,
        out double value,
        out string error)
    {
        if (DirectCurrentParameterRules.TryParseValidated(kind, text, out value, out var parameterError))
        {
            error = string.Empty;
            return true;
        }

        error = $"{channelName}：{parameterError}";
        return false;
    }
}
