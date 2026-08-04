using RuinaoHardwareEngineer.Features.RawStimulation;
using RuinaoTesHardware;
using RuinaoTesProtocol.V14;
using Xunit;

namespace RuinaoHardwareEngineer.Tests;

public sealed class UsbTest4RawStimulationChannelCommandTests
{
    [Fact]
    public async Task StartAndStopChannelAsync_ChannelThree_WriteExpectedSingleBitMasks()
    {
        var transport = new RecordingStatusTransport();
        await using var client = new BackplaneClient(new EmptyDiscovery(), transport);
        var service = new UsbTest4RawStimulationService(client);
        var options = new BackplaneConnectionOptions(0x01, TimeSpan.FromSeconds(1));

        await service.StartChannelAsync(
            0x01,
            3,
            options,
            TestContext.Current.CancellationToken);
        await service.StopChannelAsync(
            0x01,
            3,
            options,
            TestContext.Current.CancellationToken);

        Assert.Collection(
            transport.Requests.Select(DecodeSingleRegister),
            register =>
            {
                Assert.Equal(0x0002, register.Address);
                Assert.Equal(0x00000004U, register.Value);
            },
            register =>
            {
                Assert.Equal(0x0003, register.Address);
                Assert.Equal(0x00000004U, register.Value);
            });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    public async Task StartChannelAsync_InvalidChannel_RejectsBeforeSending(int channel)
    {
        var transport = new RecordingStatusTransport();
        await using var client = new BackplaneClient(new EmptyDiscovery(), transport);
        var service = new UsbTest4RawStimulationService(client);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => service.StartChannelAsync(
                0x01,
                channel,
                new BackplaneConnectionOptions(0x01, TimeSpan.FromSeconds(1)),
                TestContext.Current.CancellationToken));

        Assert.Empty(transport.Requests);
    }

    private static TesV14RegisterValue DecodeSingleRegister(byte[] request)
    {
        Assert.True(
            TesV14ProtocolCodec.TryParseFrame(request, out var frame, out var frameError),
            frameError);
        Assert.NotNull(frame);
        Assert.True(
            TesV14RegisterPayloadCodec.TryDecode(frame.Payload, out var registers, out var payloadError),
            payloadError);
        return Assert.Single(registers);
    }

    private sealed class EmptyDiscovery : IUsbBackplaneDiscovery
    {
        public Task<UsbBackplaneDevice?> FindAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<UsbBackplaneDevice?>(null);
    }

    private sealed class RecordingStatusTransport : IBackplaneTransport
    {
        public bool IsOpen => true;
        public List<byte[]> Requests { get; } = [];

        public Task OpenAsync(
            UsbBackplaneDevice device,
            TimeSpan timeout,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<byte[]> ExchangeAsync(
            ReadOnlyMemory<byte> request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request.ToArray());
            Assert.True(
                TesV14ProtocolCodec.TryParseFrame(request.Span, out var requestFrame, out var error),
                error);
            Assert.NotNull(requestFrame);
            return Task.FromResult(TesV14ProtocolCodec.BuildFrame(
                TesV14FrameControl.None,
                TesV14Command.Response,
                requestFrame.DestinationAddress,
                requestFrame.SourceAddress,
                27,
                requestFrame.SendSequence,
                [0, 0, 0, 0],
                requestFrame.Version));
        }

        public Task CloseAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public ValueTask DisposeAsync() =>
            ValueTask.CompletedTask;
    }
}
