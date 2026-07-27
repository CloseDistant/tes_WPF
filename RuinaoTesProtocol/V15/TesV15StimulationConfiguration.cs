using RuinaoTesProtocol.V14;

namespace RuinaoTesProtocol.V15;

/// <summary>V1.5 电刺激波形类型。两种产品刺激模式均由梯形和可选的零输出定值段组成。</summary>
public enum TesV15StimulationMode : uint
{
    Constant = 1,
    DirectCurrentTrapezoid = 8,
}

/// <summary>V1.5 单段波形固定占用的 16 个 32-bit 寄存器。</summary>
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
/// V1.5 单通道刺激配置。连续模式使用一段梯形；间隔模式使用梯形与零输出定值段。
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

/// <summary>按 V1.5 文档与 usbtest2 实际代码生成电刺激寄存器。</summary>
public static class TesV15StimulationRegisterCodec
{
    public const uint UsbTest2ConfigurationVersion = 0x15;
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
        return CreateDirectCurrent(
            channelNumber,
            totalTimeMs,
            checked(totalTimeMs * 1000U),
            0,
            lowLevel,
            highLevel,
            risePermille,
            holdPermille,
            fallPermille,
            validationMode);
    }

    public static TesV15StimulationConfiguration CreateDirectCurrent(
        byte channelNumber,
        uint totalTimeMs,
        uint activeDurationUs,
        uint intervalDurationUs,
        uint lowLevel,
        uint highLevel,
        uint risePermille,
        uint holdPermille,
        uint fallPermille,
        TesV15ParameterValidationMode validationMode = TesV15ParameterValidationMode.RecommendedRange)
    {
        ValidateChannelAndTime(channelNumber, totalTimeMs);
        ValidateDacValue(lowLevel, nameof(lowLevel), validationMode);
        ValidateDacValue(highLevel, nameof(highLevel), validationMode);
        ValidateTrapezoidPermille(risePermille, holdPermille, fallPermille);
        return CreateTrapezoidProgram(
            channelNumber,
            totalTimeMs,
            activeDurationUs,
            intervalDurationUs,
            lowLevel,
            highLevel,
            risePermille,
            holdPermille,
            fallPermille);
    }

    /// <summary>
    /// tPCS 产品模式同样编码为梯形：渐升段 + 平台脉冲段 + 0时长渐降，
    /// 并可追加一段零输出定值作为脉冲间隔；不再使用 waveform_type=10。
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

        var activeDurationUs = checked(riseDurationUs + plateauDurationUs);
        var risePermille = checked((uint)decimal.Round(
            riseDurationUs * 1000M / activeDurationUs,
            0,
            MidpointRounding.AwayFromZero));
        var holdPermille = 1000U - risePermille;
        return CreateTrapezoidProgram(
            channelNumber,
            totalTimeMs,
            activeDurationUs,
            intervalDurationUs,
            lowLevel,
            highLevel,
            risePermille,
            holdPermille,
            0);
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
        uint activeDurationUs,
        uint intervalDurationUs,
        uint baselineLevel,
        uint targetLevel,
        uint risePermille,
        uint holdPermille,
        uint fallPermille)
    {
        if (activeDurationUs == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(activeDurationUs), "梯形刺激段时长必须大于0微秒。");
        }

        var totalTimeUs = (ulong)totalTimeMs * 1000UL;
        if (activeDurationUs > totalTimeUs)
        {
            throw new ArgumentException("单次刺激时长不能超过刺激总时间。");
        }

        var cycleDurationUs = (ulong)activeDurationUs + intervalDurationUs;
        if (intervalDurationUs > 0 && cycleDurationUs > totalTimeUs)
        {
            throw new ArgumentException("单次刺激时长与间隔时长之和不能超过刺激总时间。");
        }

        var waveforms = new List<TesV15StimulationWaveform>(2)
        {
            new(
                TesV15StimulationMode.DirectCurrentTrapezoid,
                activeDurationUs,
                0,
                0,
                0,
                0,
                0,
                baselineLevel,
                targetLevel,
                risePermille,
                holdPermille,
                fallPermille,
                0,
                0,
                1,
                0),
        };
        if (intervalDurationUs > 0)
        {
            waveforms.Add(new TesV15StimulationWaveform(
                TesV15StimulationMode.Constant,
                intervalDurationUs,
                0,
                0,
                baselineLevel,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                1,
                0));
        }

        var requiresLoop = intervalDurationUs > 0 || totalTimeUs > activeDurationUs;
        return new TesV15StimulationConfiguration(
            1U << (channelNumber - 1),
            UsbTest2ConfigurationVersion,
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
            throw new ArgumentOutOfRangeException(parameterName, "usbtest2刺激DAC值必须在0到60000之间。");
        }
    }

    private static void ValidateTrapezoidPermille(uint rise, uint hold, uint fall)
    {
        if (rise > 1000 || hold > 1000 || fall > 1000 || rise + hold + fall != 1000)
        {
            throw new ArgumentException("梯形的上升、平台和下降占比必须各在0到1000之间且总和等于1000。");
        }
    }
}
