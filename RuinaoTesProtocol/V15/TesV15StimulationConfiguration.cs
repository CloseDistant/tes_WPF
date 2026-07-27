using RuinaoTesProtocol.V14;

namespace RuinaoTesProtocol.V15;

/// <summary>V1.5 电刺激波形类型。两种产品刺激模式均使用类型8梯形。</summary>
public enum TesV15StimulationMode : uint
{
    Constant = 1,
    DirectCurrentTrapezoid = 8,
}

/// <summary>
/// V1.5 单段波形固定占用的 16 个 32-bit 寄存器。
/// 类型8使用 CustomIdOrSeedOrPeriodIntervalUs 字段承载低平台阶段的千分比。
/// </summary>
public sealed record TesV15StimulationWaveform(
    TesV15StimulationMode Mode,
    uint DurationUs,
    uint FrequencyHz,
    uint Amplitude,
    uint Offset,
    uint PhaseDegree,
    uint DutyPermilleOrOrder,
    uint LowLevelOrPositiveValue,
    uint HighLevelOrNegativeValue,
    uint RisePermilleOrPositiveDurationUs,
    uint HoldPermilleOrInterphaseIntervalUs,
    uint FallPermilleOrNegativeDurationUs,
    uint CustomIdOrSeedOrPeriodIntervalUs,
    uint SampleCount,
    uint RepeatCount,
    uint Flags);

/// <summary>
/// V1.5 单通道刺激配置。类型8内部包含上升、高平台、下降和低平台四个阶段。
/// </summary>
public sealed record TesV15StimulationConfiguration(
    uint EnableMask,
    uint ConfigVersion,
    byte ChannelNumber,
    uint TriggerEnable,
    uint TriggerSource,
    uint TotalTimeMs,
    uint ChannelFlags,
    IReadOnlyList<TesV15StimulationWaveform> Waveforms);

/// <summary>按 V1.5 文档与 usbtest3 实际代码生成电刺激寄存器。</summary>
public static class TesV15StimulationRegisterCodec
{
    public const uint UsbTestConfigurationVersion = 0x15;
    public const ushort EnableMaskRegister = 0x2E00;
    public const ushort ConfigurationVersionRegister = 0x2E01;
    public const ushort ConfigurationStatusRegister = 0x2E02;
    public const ushort RunStateRegister = 0x2E03;
    public const ushort StartRegister = 0x0002;
    public const ushort StopRegister = 0x0003;
    public const int MaximumWaveformCount = 30;

    public static TesV15StimulationConfiguration CreateDirectCurrent(
        byte channelNumber,
        uint totalTimeMs,
        uint lowLevel,
        uint highLevel,
        uint risePermille,
        uint holdPermille,
        uint fallPermille,
        TesV15ParameterValidationMode validationMode = TesV15ParameterValidationMode.RecommendedRange)
    {
        return CreateDirectCurrentCycle(
            channelNumber,
            totalTimeMs,
            checked(totalTimeMs * 1000U),
            lowLevel,
            highLevel,
            risePermille,
            holdPermille,
            fallPermille,
            0,
            validationMode);
    }

    public static TesV15StimulationConfiguration CreateDirectCurrentCycle(
        byte channelNumber,
        uint totalTimeMs,
        uint cycleDurationUs,
        uint lowLevel,
        uint highLevel,
        uint risePermille,
        uint highHoldPermille,
        uint fallPermille,
        uint lowHoldPermille,
        TesV15ParameterValidationMode validationMode = TesV15ParameterValidationMode.RecommendedRange)
    {
        ValidateChannelAndTime(channelNumber, totalTimeMs);
        ValidateDacValue(lowLevel, nameof(lowLevel), validationMode);
        ValidateDacValue(highLevel, nameof(highLevel), validationMode);
        ValidateTrapezoidCyclePermille(
            risePermille,
            highHoldPermille,
            fallPermille,
            lowHoldPermille);
        return CreateTrapezoidProgram(
            channelNumber,
            totalTimeMs,
            cycleDurationUs,
            lowLevel,
            highLevel,
            risePermille,
            highHoldPermille,
            fallPermille,
            lowHoldPermille);
    }

    /// <summary>
    /// tPCS 产品模式同样编码为梯形：渐升段 + 平台脉冲段 + 0时长渐降 + 低平台间隔；
    /// 不再使用 waveform_type=10，也不再为间隔追加类型1波形。
    /// </summary>
    public static TesV15StimulationConfiguration CreatePulseCurrent(
        byte channelNumber,
        uint totalTimeMs,
        uint lowLevel,
        uint highLevel,
        uint riseDurationUs,
        uint plateauDurationUs,
        uint intervalDurationUs,
        TesV15ParameterValidationMode validationMode = TesV15ParameterValidationMode.RecommendedRange)
    {
        ValidateChannelAndTime(channelNumber, totalTimeMs);
        ValidateDacValue(lowLevel, nameof(lowLevel), validationMode);
        ValidateDacValue(highLevel, nameof(highLevel), validationMode);
        if (riseDurationUs == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(riseDurationUs),
                "tPCS上升宽度必须大于0微秒。");
        }

        if (plateauDurationUs == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(plateauDurationUs),
                "tPCS脉冲平台宽度必须大于0微秒。");
        }

        var cycleDurationUs = checked(riseDurationUs + plateauDurationUs + intervalDurationUs);
        var cyclePermille = TesV15EngineeringUnitConverter.ToTrapezoidCyclePermille(
            riseDurationUs,
            plateauDurationUs,
            0,
            intervalDurationUs);
        return CreateTrapezoidProgram(
            channelNumber,
            totalTimeMs,
            cycleDurationUs,
            lowLevel,
            highLevel,
            cyclePermille.RisePermille,
            cyclePermille.HighHoldPermille,
            cyclePermille.FallPermille,
            cyclePermille.LowHoldPermille);
    }

    public static IReadOnlyList<TesV14RegisterValue> BuildControlRegisters(
        TesV15StimulationConfiguration configuration)
    {
        ValidateConfiguration(configuration);
        var channelBase = GetChannelBase(configuration.ChannelNumber);

        return
        [
            new(EnableMaskRegister, configuration.EnableMask),
            new(ConfigurationVersionRegister, configuration.ConfigVersion),
            new(channelBase, (uint)(configuration.ChannelNumber - 1)),
            new((ushort)(channelBase + 0x01), configuration.TriggerEnable),
            new((ushort)(channelBase + 0x02), configuration.TriggerSource),
            new((ushort)(channelBase + 0x03), configuration.TotalTimeMs),
            new((ushort)(channelBase + 0x04), (uint)configuration.Waveforms.Count),
            new((ushort)(channelBase + 0x05), configuration.ChannelFlags),
        ];
    }

    public static IReadOnlyList<TesV14RegisterValue> BuildWaveformRegisters(
        TesV15StimulationConfiguration configuration,
        int waveformIndex)
    {
        ValidateConfiguration(configuration);
        if (waveformIndex < 0 || waveformIndex >= configuration.Waveforms.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(waveformIndex), "波形序号超出配置范围。");
        }

        var waveform = configuration.Waveforms[waveformIndex];
        var waveBase = checked((ushort)(
            GetChannelBase(configuration.ChannelNumber) + 0x20 + waveformIndex * 0x10));

        return
        [
            new(waveBase, (uint)waveform.Mode),
            new((ushort)(waveBase + 0x01), waveform.DurationUs),
            new((ushort)(waveBase + 0x02), waveform.FrequencyHz),
            new((ushort)(waveBase + 0x03), waveform.Amplitude),
            new((ushort)(waveBase + 0x04), waveform.Offset),
            new((ushort)(waveBase + 0x05), waveform.PhaseDegree),
            new((ushort)(waveBase + 0x06), waveform.DutyPermilleOrOrder),
            new((ushort)(waveBase + 0x07), waveform.LowLevelOrPositiveValue),
            new((ushort)(waveBase + 0x08), waveform.HighLevelOrNegativeValue),
            new((ushort)(waveBase + 0x09), waveform.RisePermilleOrPositiveDurationUs),
            new((ushort)(waveBase + 0x0A), waveform.HoldPermilleOrInterphaseIntervalUs),
            new((ushort)(waveBase + 0x0B), waveform.FallPermilleOrNegativeDurationUs),
            new((ushort)(waveBase + 0x0C), waveform.CustomIdOrSeedOrPeriodIntervalUs),
            new((ushort)(waveBase + 0x0D), waveform.SampleCount),
            new((ushort)(waveBase + 0x0E), waveform.RepeatCount),
            new((ushort)(waveBase + 0x0F), waveform.Flags),
        ];
    }

    public static ushort GetChannelBase(byte channelNumber)
    {
        ValidateChannel(channelNumber);
        return (ushort)(0x3000 + (channelNumber - 1) * 0x0200);
    }

    private static TesV15StimulationConfiguration CreateTrapezoidProgram(
        byte channelNumber,
        uint totalTimeMs,
        uint cycleDurationUs,
        uint baselineLevel,
        uint targetLevel,
        uint risePermille,
        uint highHoldPermille,
        uint fallPermille,
        uint lowHoldPermille)
    {
        if (cycleDurationUs == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cycleDurationUs), "梯形完整周期必须大于0微秒。");
        }

        var totalTimeUs = (ulong)totalTimeMs * 1000UL;
        if (cycleDurationUs > totalTimeUs)
        {
            throw new ArgumentException("完整刺激周期不能超过刺激总时间。");
        }

        IReadOnlyList<TesV15StimulationWaveform> waveforms =
        [
            new(
                TesV15StimulationMode.DirectCurrentTrapezoid,
                cycleDurationUs,
                0,
                0,
                0,
                0,
                0,
                baselineLevel,
                targetLevel,
                risePermille,
                highHoldPermille,
                fallPermille,
                lowHoldPermille,
                0,
                1,
                0),
        ];

        var requiresLoop = totalTimeUs > cycleDurationUs;
        return new TesV15StimulationConfiguration(
            1U << (channelNumber - 1),
            UsbTestConfigurationVersion,
            channelNumber,
            0,
            0,
            totalTimeMs,
            requiresLoop ? 1U : 0U,
            waveforms);
    }

    private static void ValidateConfiguration(TesV15StimulationConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ValidateChannelAndTime(configuration.ChannelNumber, configuration.TotalTimeMs);
        if (configuration.Waveforms is null
            || configuration.Waveforms.Count is < 1 or > MaximumWaveformCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(configuration),
                $"单通道波形数量必须在1到{MaximumWaveformCount}之间。");
        }

        var expectedMask = 1U << (configuration.ChannelNumber - 1);
        if ((configuration.EnableMask & expectedMask) == 0)
        {
            throw new ArgumentException("通道使能掩码未包含当前刺激通道。", nameof(configuration));
        }
    }

    private static void ValidateChannelAndTime(byte channelNumber, uint totalTimeMs)
    {
        ValidateChannel(channelNumber);
        if (totalTimeMs == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(totalTimeMs), "总运行时间必须大于0毫秒。");
        }

        if (totalTimeMs > uint.MaxValue / 1000U)
        {
            throw new ArgumentOutOfRangeException(
                nameof(totalTimeMs),
                "总运行时间过大，无法转换为微秒。");
        }
    }

    private static void ValidateChannel(byte channelNumber)
    {
        if (channelNumber is < 1 or > 8)
        {
            throw new ArgumentOutOfRangeException(nameof(channelNumber), "电刺激通道必须在1到8之间。");
        }
    }

    private static void ValidateDacValue(
        uint value,
        string parameterName,
        TesV15ParameterValidationMode validationMode)
    {
        if (validationMode == TesV15ParameterValidationMode.RecommendedRange
            && value > 60000)
        {
            throw new ArgumentOutOfRangeException(parameterName, "usbtest兼容刺激DAC值必须在0到60000之间。");
        }
    }

    private static void ValidateTrapezoidCyclePermille(
        uint rise,
        uint highHold,
        uint fall,
        uint lowHold)
    {
        var sum = (ulong)rise + highHold + fall + lowHold;
        if (rise > 1000
            || highHold > 1000
            || fall > 1000
            || lowHold > 1000
            || sum != 1000)
        {
            throw new ArgumentException(
                "类型8的上升、高平台、下降和低平台占比必须各在0到1000之间且总和等于1000。");
        }
    }
}
