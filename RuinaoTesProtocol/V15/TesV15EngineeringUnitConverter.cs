namespace RuinaoTesProtocol.V15;

public enum TesV15ParameterValidationMode
{
    RecommendedRange,
    ProtocolRange,
}

/// <summary>
/// 工程师软件输入单位到 V1.5 寄存器单位的确定性转换。
/// tDCS 的 DAC 比例属于硬件标定值，必须由调用方明确提供。
/// </summary>
public static class TesV15EngineeringUnitConverter
{
    public const decimal MaximumCurrentMilliampere = 15M;

    public static uint MilliampereToMicroampere(
        decimal currentMilliampere,
        TesV15ParameterValidationMode validationMode = TesV15ParameterValidationMode.RecommendedRange)
    {
        ValidateCurrent(currentMilliampere, validationMode);
        var microampere = decimal.Round(
            currentMilliampere * 1000M,
            0,
            MidpointRounding.AwayFromZero);
        if (microampere > uint.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(currentMilliampere),
                "电流换算结果超出协议uint32可表示范围。");
        }

        return checked((uint)microampere);
    }

    public static uint SecondsToMilliseconds(decimal seconds, string parameterName)
    {
        return ConvertTime(seconds, 1000M, parameterName);
    }

    public static uint SecondsToMicroseconds(decimal seconds, string parameterName)
    {
        return ConvertTime(seconds, 1_000_000M, parameterName);
    }

    public static uint MillisecondsToMicroseconds(decimal milliseconds, string parameterName)
    {
        return ConvertTime(milliseconds, 1000M, parameterName);
    }

    public static (uint RisePermille, uint HoldPermille, uint FallPermille) ToTrapezoidPermille(
        decimal totalSeconds,
        decimal rampUpSeconds,
        decimal rampDownSeconds)
    {
        if (totalSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(totalSeconds), "总运行时间必须大于0秒。");
        }

        if (rampUpSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rampUpSeconds), "渐升时间不能小于0秒。");
        }

        if (rampDownSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rampDownSeconds), "渐降时间不能小于0秒。");
        }

        if (rampUpSeconds + rampDownSeconds > totalSeconds)
        {
            throw new ArgumentException("渐升时间与渐降时间之和不能超过总运行时间。");
        }

        var rise = checked((uint)decimal.Round(
            rampUpSeconds * 1000M / totalSeconds,
            0,
            MidpointRounding.AwayFromZero));
        var fall = checked((uint)decimal.Round(
            rampDownSeconds * 1000M / totalSeconds,
            0,
            MidpointRounding.AwayFromZero));
        if (rise + fall > 1000)
        {
            // 两次独立四舍五入最多产生1个千分点的进位误差，优先保留渐升比例。
            fall = 1000 - rise;
        }

        return (rise, 1000 - rise - fall, fall);
    }

    public static (uint BaselineDac, uint TargetDac) DirectCurrentToDac(
        decimal currentMilliampere,
        uint zeroCurrentDac,
        decimal dacCountsPerMilliampere,
        bool reversePolarity,
        TesV15ParameterValidationMode validationMode = TesV15ParameterValidationMode.RecommendedRange)
    {
        ValidateCurrent(currentMilliampere, validationMode);
        if (validationMode == TesV15ParameterValidationMode.RecommendedRange
            && zeroCurrentDac > 60000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(zeroCurrentDac),
                "零电流DAC值必须在0到60000之间。");
        }

        if (dacCountsPerMilliampere <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(dacCountsPerMilliampere),
                "每mA对应的DAC计数必须大于0，并由硬件标定提供。");
        }

        var delta = decimal.Round(
            currentMilliampere * dacCountsPerMilliampere,
            0,
            MidpointRounding.AwayFromZero);
        var target = reversePolarity ? zeroCurrentDac - delta : zeroCurrentDac + delta;
        if (target < 0 || target > uint.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(currentMilliampere),
                $"换算后的目标DAC值{target}超出协议uint32可表示范围。");
        }

        if (validationMode == TesV15ParameterValidationMode.RecommendedRange
            && target > 60000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(currentMilliampere),
                $"换算后的目标DAC值{target}超出0到60000范围。");
        }

        return (zeroCurrentDac, checked((uint)target));
    }

    private static uint ConvertTime(decimal value, decimal multiplier, string parameterName)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, $"{parameterName}不能小于0。");
        }

        var converted = decimal.Round(value * multiplier, 0, MidpointRounding.AwayFromZero);
        if (converted > uint.MaxValue)
        {
            throw new ArgumentOutOfRangeException(parameterName, $"{parameterName}超出协议可表示范围。");
        }

        return checked((uint)converted);
    }

    private static void ValidateCurrent(
        decimal currentMilliampere,
        TesV15ParameterValidationMode validationMode)
    {
        if (currentMilliampere <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(currentMilliampere),
                "电流必须大于0mA。");
        }

        if (validationMode == TesV15ParameterValidationMode.RecommendedRange
            && currentMilliampere > MaximumCurrentMilliampere)
        {
            throw new ArgumentOutOfRangeException(
                nameof(currentMilliampere),
                $"电流必须大于0且不超过{MaximumCurrentMilliampere}mA。");
        }
    }
}
