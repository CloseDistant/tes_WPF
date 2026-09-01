using RuinaoTesProtocol.V14;

namespace RuinaoTesHardware;

internal sealed record StimulationWaveformHardwareSegment(
    uint WaveformType,
    uint DurationMicroseconds,
    uint FrequencyHz,
    uint Amplitude,
    uint Offset,
    uint PhaseDegree,
    uint DutyPermilleOrOrder,
    int LowLevelOrPositiveValue,
    int HighLevelOrNegativeValue,
    uint RisePermilleOrPositiveDurationMicroseconds,
    uint HoldPermilleOrInterphaseIntervalMicroseconds,
    uint FallPermilleOrNegativeDurationMicroseconds,
    uint CustomIdOrSeedOrPeriodIntervalMicroseconds,
    uint SampleCount,
    uint RepeatCount,
    uint Flags);

internal sealed record CompositeStimulationHardwarePlan(
    byte BoardAddress,
    int Channel,
    uint EnableMask,
    uint ConfigurationVersion,
    uint TotalTimeMilliseconds,
    IReadOnlyList<StimulationWaveformHardwareSegment> Waveforms);

/// <summary>
/// 电刺激产品模式共用的组合波形寄存器写入器。
/// 产品客户端负责参数语义和安全校验，本类型只负责确定性的V1.6硬件布局。
/// </summary>
internal sealed class CompositeStimulationHardwareWriter
{
    private const ushort StartRegister = 0x0002;
    private const ushort StopRegister = 0x0003;
    private const int MaximumWaveformCount = 30;
    private readonly BackplaneClient client;

    public CompositeStimulationHardwareWriter(BackplaneClient client)
    {
        this.client = client;
    }

    public Task<BackplaneRegisterOperationResult> WriteWaveformAsync(
        CompositeStimulationHardwarePlan plan,
        int waveformIndex,
        BackplaneConnectionOptions options,
        CancellationToken cancellationToken)
    {
        ValidatePlan(plan);
        if (waveformIndex < 0 || waveformIndex >= plan.Waveforms.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(waveformIndex), "波形序号超出配置范围。");
        }

        return client.WriteRegistersAsync(
            plan.BoardAddress,
            BuildWaveformRegisters(plan, waveformIndex),
            options,
            cancellationToken);
    }

    public Task<BackplaneRegisterOperationResult> WriteControlAsync(
        CompositeStimulationHardwarePlan plan,
        BackplaneConnectionOptions options,
        CancellationToken cancellationToken)
    {
        ValidatePlan(plan);
        return client.WriteRegistersAsync(
            plan.BoardAddress,
            BuildControlRegisters(plan),
            options,
            cancellationToken);
    }

    public Task<BackplaneRegisterOperationResult> StartAsync(
        byte boardAddress,
        uint channelMask,
        BackplaneConnectionOptions options,
        CancellationToken cancellationToken) =>
        WriteBusinessBoardCommandAsync(
            boardAddress,
            StartRegister,
            channelMask,
            options,
            cancellationToken);

    public Task<BackplaneRegisterOperationResult> StopAsync(
        byte boardAddress,
        uint channelMask,
        BackplaneConnectionOptions options,
        CancellationToken cancellationToken) =>
        WriteBusinessBoardCommandAsync(
            boardAddress,
            StopRegister,
            channelMask,
            options,
            cancellationToken);

    public Task<BackplaneRegisterOperationResult> EmergencyStopBackplaneAsync(
        BackplaneConnectionOptions options,
        CancellationToken cancellationToken) =>
        client.WriteRegistersAsync(
            TesV14ProtocolConstants.BackplaneAddress,
            [new TesV14RegisterValue(StopRegister, 0)],
            options,
            cancellationToken);

    public static uint CreateSingleChannelMask(int channel)
    {
        ValidateChannel(channel);
        return 1U << (channel - 1);
    }

    public static void ValidateBoardAddress(byte boardAddress)
    {
        if (boardAddress > 0x07)
        {
            throw new ArgumentOutOfRangeException(
                nameof(boardAddress),
                "业务板地址必须在0x00～0x07之间。");
        }
    }

    public static void ValidateChannel(int channel)
    {
        if (channel is < 1 or > 8)
        {
            throw new ArgumentOutOfRangeException(
                nameof(channel),
                "刺激通道必须在1到8之间。");
        }
    }

    public static void ValidateChannelMask(uint channelMask)
    {
        if (channelMask == 0 || (channelMask & 0xFFFFFF00U) != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(channelMask),
                "刺激通道掩码必须至少选择CH1～CH8中的一个通道，且不得包含高24位。");
        }
    }

    private async Task<BackplaneRegisterOperationResult> WriteBusinessBoardCommandAsync(
        byte boardAddress,
        ushort registerAddress,
        uint value,
        BackplaneConnectionOptions options,
        CancellationToken cancellationToken)
    {
        ValidateBoardAddress(boardAddress);
        return await client.WriteRegistersAsync(
            boardAddress,
            [new TesV14RegisterValue(registerAddress, value)],
            options,
            cancellationToken);
    }

    private static IReadOnlyList<TesV14RegisterValue> BuildWaveformRegisters(
        CompositeStimulationHardwarePlan plan,
        int waveformIndex)
    {
        var waveform = plan.Waveforms[waveformIndex];
        var waveBase = checked((ushort)(GetChannelBase(plan.Channel) + 0x20 + waveformIndex * 0x10));
        return
        [
            new(waveBase, waveform.WaveformType),
            new((ushort)(waveBase + 0x01), waveform.DurationMicroseconds),
            new((ushort)(waveBase + 0x02), waveform.FrequencyHz),
            new((ushort)(waveBase + 0x03), waveform.Amplitude),
            new((ushort)(waveBase + 0x04), waveform.Offset),
            new((ushort)(waveBase + 0x05), waveform.PhaseDegree),
            new((ushort)(waveBase + 0x06), waveform.DutyPermilleOrOrder),
            new((ushort)(waveBase + 0x07), unchecked((uint)waveform.LowLevelOrPositiveValue)),
            new((ushort)(waveBase + 0x08), unchecked((uint)waveform.HighLevelOrNegativeValue)),
            new((ushort)(waveBase + 0x09), waveform.RisePermilleOrPositiveDurationMicroseconds),
            new((ushort)(waveBase + 0x0A), waveform.HoldPermilleOrInterphaseIntervalMicroseconds),
            new((ushort)(waveBase + 0x0B), waveform.FallPermilleOrNegativeDurationMicroseconds),
            new((ushort)(waveBase + 0x0C), waveform.CustomIdOrSeedOrPeriodIntervalMicroseconds),
            new((ushort)(waveBase + 0x0D), waveform.SampleCount),
            new((ushort)(waveBase + 0x0E), waveform.RepeatCount),
            new((ushort)(waveBase + 0x0F), waveform.Flags),
        ];
    }

    private static IReadOnlyList<TesV14RegisterValue> BuildControlRegisters(
        CompositeStimulationHardwarePlan plan)
    {
        var channelBase = GetChannelBase(plan.Channel);
        return
        [
            new(0x2E00, plan.EnableMask),
            new(0x2E01, plan.ConfigurationVersion),
            new(channelBase, (uint)(plan.Channel - 1)),
            new((ushort)(channelBase + 0x01), 0),
            new((ushort)(channelBase + 0x02), 0),
            new((ushort)(channelBase + 0x03), plan.TotalTimeMilliseconds),
            new((ushort)(channelBase + 0x04), (uint)plan.Waveforms.Count),
            new((ushort)(channelBase + 0x05), 0),
        ];
    }

    private static void ValidatePlan(CompositeStimulationHardwarePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ValidateBoardAddress(plan.BoardAddress);
        ValidateChannel(plan.Channel);
        ValidateChannelMask(plan.EnableMask);
        if (plan.Waveforms.Count is < 1 or > MaximumWaveformCount)
        {
            throw new ArgumentException(
                $"单通道必须配置1到{MaximumWaveformCount}段波形。",
                nameof(plan));
        }

        if ((plan.EnableMask & (1U << (plan.Channel - 1))) == 0)
        {
            throw new ArgumentException("通道使能掩码没有包含当前刺激通道。", nameof(plan));
        }
    }

    private static ushort GetChannelBase(int channel) =>
        checked((ushort)(0x3000 + (channel - 1) * 0x0200));
}
