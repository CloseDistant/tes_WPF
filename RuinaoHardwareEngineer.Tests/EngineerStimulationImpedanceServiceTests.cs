using RuinaoHardwareEngineer.Features.StimulationImpedance;
using RuinaoTesHardware;
using RuinaoTesProtocol.V14;
using Xunit;

namespace RuinaoHardwareEngineer.Tests;

public sealed class EngineerStimulationImpedanceServiceTests
{
    [Fact]
    public async Task ReadAsync_ReadsEightChannelRegistersFromSelectedBoard()
    {
        var transport = new ImpedanceResponseTransport();
        await using var client = new BackplaneClient(new EmptyDiscovery(), transport);
        var service = new EngineerStimulationImpedanceService(client);

        var result = await service.ReadAsync(
            0x01,
            new BackplaneConnectionOptions(0x01, TimeSpan.FromSeconds(5)),
            TestContext.Current.CancellationToken);

        Assert.Equal((byte)0x01, transport.TargetAddress);
        Assert.Equal(
            Enumerable.Range(0x1001, 8).Select(value => (ushort)value),
            transport.RequestedAddresses);
        Assert.Equal(8, result.Channels.Count);
        Assert.Collection(
            result.Channels,
            channel => AssertChannel(channel, 1, 0x1001, 101),
            channel => AssertChannel(channel, 2, 0x1002, 102),
            channel => AssertChannel(channel, 3, 0x1003, 103),
            channel => AssertChannel(channel, 4, 0x1004, 104),
            channel => AssertChannel(channel, 5, 0x1005, 105),
            channel => AssertChannel(channel, 6, 0x1006, 106),
            channel => AssertChannel(channel, 7, 0x1007, 107),
            channel => AssertChannel(channel, 8, 0x1008, 108));
    }

    [Fact]
    public async Task ReadAsync_WhenBoardAddressIsOutsideEightSlots_RejectsRequest()
    {
        await using var client = new BackplaneClient(
            new EmptyDiscovery(),
            new ImpedanceResponseTransport());
        var service = new EngineerStimulationImpedanceService(client);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.ReadAsync(
            0x08,
            new BackplaneConnectionOptions(0x01, TimeSpan.FromSeconds(5)),
            TestContext.Current.CancellationToken));
    }

    private static void AssertChannel(
        EngineerStimulationImpedanceChannel channel,
        int expectedChannel,
        ushort expectedAddress,
        uint expectedValue)
    {
        Assert.Equal(expectedChannel, channel.Channel);
        Assert.Equal(expectedAddress, channel.RegisterAddress);
        Assert.Equal(expectedValue, channel.RawValue);
        Assert.Equal(expectedValue / 100m, channel.ImpedanceOhms);
    }

    private sealed class EmptyDiscovery : IUsbBackplaneDiscovery
    {
        public Task<UsbBackplaneDevice?> FindAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<UsbBackplaneDevice?>(null);
    }

    private sealed class ImpedanceResponseTransport : IBackplaneTransport
    {
        public bool IsOpen => true;
        public byte TargetAddress { get; private set; }
        public IReadOnlyList<ushort> RequestedAddresses { get; private set; } = Array.Empty<ushort>();

        public Task OpenAsync(
            UsbBackplaneDevice device,
            TimeSpan timeout,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<byte[]> ExchangeAsync(
            ReadOnlyMemory<byte> request,
            CancellationToken cancellationToken = default)
        {
            Assert.True(
                TesV14ProtocolCodec.TryParseFrame(request.Span, out var requestFrame, out var error),
                error);
            Assert.NotNull(requestFrame);
            Assert.True(
                TesV14RegisterPayloadCodec.TryDecode(
                    requestFrame.Payload,
                    out var requestedRegisters,
                    out error),
                error);

            TargetAddress = requestFrame.DestinationAddress;
            RequestedAddresses = requestedRegisters.Select(register => register.Address).ToArray();
            var responseRegisters = requestedRegisters
                .Select((register, index) => new TesV14RegisterValue(
                    register.Address,
                    checked((uint)(101 + index))))
                .ToArray();
            var payload = TesV14RegisterPayloadCodec.Encode(responseRegisters);
            return Task.FromResult(TesV14ProtocolCodec.BuildFrame(
                TesV14FrameControl.None,
                TesV14Command.Response,
                requestFrame.DestinationAddress,
                requestFrame.SourceAddress,
                1,
                requestFrame.SendSequence,
                payload,
                requestFrame.Version));
        }

        public Task CloseAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public ValueTask DisposeAsync() =>
            ValueTask.CompletedTask;
    }
}
