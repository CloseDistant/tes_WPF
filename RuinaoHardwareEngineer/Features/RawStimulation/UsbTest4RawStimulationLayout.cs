using RuinaoTesProtocol.V14;

namespace RuinaoHardwareEngineer.Features.RawStimulation;

/// <summary>
/// usbtest4 V1.6组合波形的工程师工具兼容布局。
/// 这些原始参数尚未形成产品级业务语义，因此暂不进入共享协议DLL。
/// </summary>
public static class UsbTest4RawStimulationLayout
{
    public const int MaximumWaveformCount = 30;
    public const ushort StartRegister = 0x0002;
    public const ushort StopRegister = 0x0003;
    public const ushort PowerSetHighRegister = 0x0005;
    public const ushort PowerSetLowRegister = 0x0006;

    public static IReadOnlyList<TesV14RegisterValue> BuildControlRegisters(
        UsbTest4RawStimulationConfiguration configuration)
    {
        Validate(configuration);
        var channelBase = GetChannelBase(configuration.Channel);
        return
        [
            new(0x2E00, configuration.EnableMask),
            new(0x2E01, configuration.ConfigVersion),
            new(channelBase, (uint)(configuration.Channel - 1)),
            new((ushort)(channelBase + 0x01), configuration.TriggerEnable),
            new((ushort)(channelBase + 0x02), configuration.TriggerSource),
            new((ushort)(channelBase + 0x03), configuration.TotalTimeMs),
            new((ushort)(channelBase + 0x04), (uint)configuration.Waveforms.Count),
            new((ushort)(channelBase + 0x05), configuration.ChannelFlags),
        ];
    }

    public static IReadOnlyList<TesV14RegisterValue> BuildWaveformRegisters(
        UsbTest4RawStimulationConfiguration configuration,
        int waveformIndex)
    {
        Validate(configuration);
        if (waveformIndex < 0 || waveformIndex >= configuration.Waveforms.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(waveformIndex), "波形序号超出配置范围。");
        }

        var waveform = configuration.Waveforms[waveformIndex];
        var waveBase = checked((ushort)(GetChannelBase(configuration.Channel) + 0x20 + waveformIndex * 0x10));
        return
        [
            new(waveBase, waveform.WaveformType),
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

    public static ushort GetChannelBase(int channel)
    {
        if (channel is < 1 or > 8)
        {
            throw new ArgumentOutOfRangeException(nameof(channel), "刺激通道必须在1到8之间。");
        }

        return checked((ushort)(0x3000 + (channel - 1) * 0x0200));
    }

    public static void Validate(UsbTest4RawStimulationConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (configuration.BoardAddress > 0x07)
        {
            throw new ArgumentOutOfRangeException(nameof(configuration), "业务板地址必须在0x00到0x07之间。");
        }

        _ = GetChannelBase(configuration.Channel);
        if (configuration.Waveforms.Count is < 1 or > MaximumWaveformCount)
        {
            throw new ArgumentException($"单通道必须配置1到{MaximumWaveformCount}段波形。", nameof(configuration));
        }

        if ((configuration.EnableMask & (1U << (configuration.Channel - 1))) == 0)
        {
            throw new ArgumentException("通道使能掩码没有包含当前刺激通道。", nameof(configuration));
        }
    }
}
