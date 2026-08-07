using RuinaoTesProtocol.V14;

namespace RuinaoTesHardware;

internal sealed record TypeEightStimulationHardwarePlan(
    byte BoardAddress,
    int Channel,
    uint EnableMask,
    uint ConfigurationVersion,
    uint WaveformType,
    uint DurationMicroseconds,
    int LowDa,
    int HighDa,
    uint RiseMicroseconds,
    uint HighHoldMicroseconds,
    uint FallMicroseconds,
    uint LowHoldMicroseconds,
    uint TotalTimeMilliseconds);

/// <summary>
/// 类型8刺激模式共用的寄存器布局和命令写入器。
/// 产品模式负责参数语义和安全校验，本类型只负责确定性的硬件布局。
/// </summary>
internal sealed class TypeEightStimulationHardwareWriter
{
    private const ushort StartRegister = 0x0002;
    private const ushort StopRegister = 0x0003;
    private readonly BackplaneClient client;

    public TypeEightStimulationHardwareWriter(BackplaneClient client)
    {
        this.client = client;
    }

    public Task<BackplaneRegisterOperationResult> WriteWaveformAsync(
        TypeEightStimulationHardwarePlan plan,
        BackplaneConnectionOptions options,
        CancellationToken cancellationToken) =>
        client.WriteRegistersAsync(
            plan.BoardAddress,
            BuildWaveformRegisters(plan),
            options,
            cancellationToken);

    public Task<BackplaneRegisterOperationResult> WriteControlAsync(
        TypeEightStimulationHardwarePlan plan,
        BackplaneConnectionOptions options,
        CancellationToken cancellationToken) =>
        client.WriteRegistersAsync(
            plan.BoardAddress,
            BuildControlRegisters(plan),
            options,
            cancellationToken);

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
        TypeEightStimulationHardwarePlan plan)
    {
        var waveBase = checked((ushort)(GetChannelBase(plan.Channel) + 0x20));
        return
        [
            new(waveBase, plan.WaveformType),
            new((ushort)(waveBase + 0x01), plan.DurationMicroseconds),
            new((ushort)(waveBase + 0x02), 0),
            new((ushort)(waveBase + 0x03), 0),
            new((ushort)(waveBase + 0x04), 0),
            new((ushort)(waveBase + 0x05), 0),
            new((ushort)(waveBase + 0x06), 0),
            new((ushort)(waveBase + 0x07), unchecked((uint)plan.LowDa)),
            new((ushort)(waveBase + 0x08), unchecked((uint)plan.HighDa)),
            new((ushort)(waveBase + 0x09), plan.RiseMicroseconds),
            new((ushort)(waveBase + 0x0A), plan.HighHoldMicroseconds),
            new((ushort)(waveBase + 0x0B), plan.FallMicroseconds),
            new((ushort)(waveBase + 0x0C), plan.LowHoldMicroseconds),
            new((ushort)(waveBase + 0x0D), 0),
            new((ushort)(waveBase + 0x0E), 1),
            new((ushort)(waveBase + 0x0F), 0),
        ];
    }

    private static IReadOnlyList<TesV14RegisterValue> BuildControlRegisters(
        TypeEightStimulationHardwarePlan plan)
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
            new((ushort)(channelBase + 0x04), 1),
            new((ushort)(channelBase + 0x05), 0),
        ];
    }

    private static ushort GetChannelBase(int channel) =>
        checked((ushort)(0x3000 + (channel - 1) * 0x0200));
}
