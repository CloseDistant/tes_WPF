using RuinaoTesHardware;

namespace RuinaoHardwareEngineer.Features.StimulationImpedance;

/// <summary>
/// 电刺激业务板阻抗读取用例。
/// 下位机自行按约2秒周期更新阻抗，本服务只读取当前快照，不发送采集启停命令。
/// </summary>
public sealed class EngineerStimulationImpedanceService
{
    private static readonly ushort[] ChannelRegisterAddresses =
        Enumerable.Range(0x1001, 8)
            .Select(address => checked((ushort)address))
            .ToArray();

    private readonly BackplaneClient client;

    public EngineerStimulationImpedanceService(BackplaneClient client)
    {
        this.client = client;
    }

    public async Task<EngineerStimulationImpedanceSnapshot> ReadAsync(
        byte boardAddress,
        BackplaneConnectionOptions options,
        CancellationToken cancellationToken = default)
    {
        if (boardAddress > 0x07)
        {
            throw new ArgumentOutOfRangeException(
                nameof(boardAddress),
                "电刺激业务板地址必须在0x00～0x07之间。");
        }

        var result = await client.ReadRegistersAsync(
            boardAddress,
            ChannelRegisterAddresses,
            options,
            cancellationToken);
        var channels = result.Registers
            .Select((register, index) => new EngineerStimulationImpedanceChannel(
                index + 1,
                register.Address,
                register.Value))
            .ToArray();

        return new EngineerStimulationImpedanceSnapshot(
            boardAddress,
            channels,
            result.Elapsed,
            DateTimeOffset.Now,
            result.RequestSequence);
    }
}
