using RuinaoTesHardware;
using RuinaoTesProtocol.V14;
using Xunit;

namespace RuinaoHardwareEngineer.Tests;

public sealed class TesHardwareDeviceImpedanceTests
{
    [Fact]
    public async Task ReadStimulationBoardImpedanceAsync_ReadsEightRegistersInChannelOrder()
    {
        var transport = new ImpedanceTransport();
        await using var backplaneClient = new BackplaneClient(new EmptyDiscovery(), transport);
        var client = new TesHardwareDeviceClient(backplaneClient);

        var result = await client.ReadStimulationBoardImpedanceAsync(
            0x03,
            TestContext.Current.CancellationToken);

        Assert.Equal((byte)0x03, result.BoardAddress);
        Assert.Equal(8, result.Channels.Count);
        Assert.Equal(
            Enumerable.Range(0x1001, 8).Select(value => (ushort)value),
            result.Channels.Select(channel => channel.RegisterAddress));
        Assert.Equal(1, result.Channels[0].PhysicalChannelNumber);
        Assert.Equal(0x0000_1447U, result.Channels[0].RawValue);
        Assert.Equal(51.91m, result.Channels[0].ImpedanceOhms);
    }

    [Fact]
    public async Task ReadStimulationBoardImpedanceAsync_RejectsAddressOutsideBackplaneSlots()
    {
        await using var backplaneClient = new BackplaneClient(new EmptyDiscovery(), new ImpedanceTransport());
        var client = new TesHardwareDeviceClient(backplaneClient);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            client.ReadStimulationBoardImpedanceAsync(0x08, TestContext.Current.CancellationToken));
    }

    private sealed class EmptyDiscovery : IUsbBackplaneDiscovery
    {
        public Task<UsbBackplaneDevice?> FindAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<UsbBackplaneDevice?>(null);
    }

    private sealed class ImpedanceTransport : IBackplaneTransport
    {
        public bool IsOpen => true;

        public Task OpenAsync(
            UsbBackplaneDevice device,
            TimeSpan timeout,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<byte[]> ExchangeAsync(
            ReadOnlyMemory<byte> request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.True(
                TesV14ProtocolCodec.TryParseFrame(request.Span, out var requestFrame, out var error),
                error);
            Assert.NotNull(requestFrame);
            Assert.Equal((byte)0x03, requestFrame.DestinationAddress);
            Assert.True(
                TesV14RegisterPayloadCodec.TryDecode(
                    requestFrame.Payload,
                    out var requestedRegisters,
                    out error),
                error);

            var responseRegisters = requestedRegisters
                .Select((register, index) => new TesV14RegisterValue(
                    register.Address,
                    index == 0 ? 0x0000_1447U : (uint)index))
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

        public Task CloseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
