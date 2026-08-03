using Xunit;

namespace RuinaoSoftwareWpf.Tests;

public sealed class StimulationImpedanceMapperTests
{
    [Fact]
    public void Map_OrdersOnlineBoardsBySlotAndMapsThemToSixteenLogicalChannels()
    {
        var topology = new DeviceTopologySnapshot(
            0b0000_1010,
            DateTimeOffset.UtcNow,
            [
                CreateSlot(slotIndex: 3, address: 0x03),
                CreateSlot(slotIndex: 1, address: 0x01),
            ]);
        var readings = new Dictionary<byte, StimulationBoardImpedanceReading>
        {
            [0x01] = CreateReading(0x01, 1000),
            [0x03] = CreateReading(0x03, 2000),
        };

        var result = StimulationImpedanceMapper.Map(topology, readings, DateTimeOffset.UtcNow);

        Assert.Equal(16, result.Channels.Count);
        Assert.Equal((byte)0x01, result.Channels[0].BoardAddress);
        Assert.Equal(1, result.Channels[0].PhysicalChannelNumber);
        Assert.Equal(10m, result.Channels[0].ImpedanceOhms);
        Assert.Equal((byte)0x03, result.Channels[8].BoardAddress);
        Assert.Equal(1, result.Channels[8].PhysicalChannelNumber);
        Assert.Equal(20m, result.Channels[8].ImpedanceOhms);
    }

    [Fact]
    public void Map_WithOneOnlineBoard_LeavesChannelsNineThroughSixteenUnavailable()
    {
        var topology = new DeviceTopologySnapshot(
            0b0000_0010,
            DateTimeOffset.UtcNow,
            [CreateSlot(slotIndex: 1, address: 0x01)]);
        var readings = new Dictionary<byte, StimulationBoardImpedanceReading>
        {
            [0x01] = CreateReading(0x01, 5191),
        };

        var result = StimulationImpedanceMapper.Map(topology, readings, DateTimeOffset.UtcNow);

        Assert.All(result.Channels.Take(8), channel => Assert.True(channel.IsAvailable));
        Assert.All(result.Channels.Skip(8), channel => Assert.False(channel.IsAvailable));
        Assert.Equal(51.91m, result.Channels[0].ImpedanceOhms);
    }

    [Fact]
    public void Map_RawZero_IsUnavailableInsteadOfZeroOhms()
    {
        var topology = new DeviceTopologySnapshot(
            0b0000_0010,
            DateTimeOffset.UtcNow,
            [CreateSlot(slotIndex: 1, address: 0x01)]);
        var readings = new Dictionary<byte, StimulationBoardImpedanceReading>
        {
            [0x01] = CreateReading(0x01, 0),
        };

        var result = StimulationImpedanceMapper.Map(topology, readings, DateTimeOffset.UtcNow);

        Assert.Equal(0U, result.Channels[0].RawValue);
        Assert.Null(result.Channels[0].ImpedanceOhms);
        Assert.False(result.Channels[0].IsAvailable);
    }

    private static DeviceTopologySlot CreateSlot(int slotIndex, byte address) =>
        new(
            slotIndex,
            address,
            true,
            true,
            DeviceBoardKind.Stimulation,
            "tES",
            [],
            TimeSpan.FromMilliseconds(10),
            "在线");

    private static StimulationBoardImpedanceReading CreateReading(byte address, uint firstRawValue) =>
        new(
            address,
            DateTimeOffset.UtcNow,
            Enumerable.Range(1, 8)
                .Select(channel => new StimulationBoardChannelReading(
                    channel,
                    checked((ushort)(0x1000 + channel)),
                    channel == 1 ? firstRawValue : (uint)channel,
                    (channel == 1 ? firstRawValue : (uint)channel) / 100m))
                .ToArray());
}
