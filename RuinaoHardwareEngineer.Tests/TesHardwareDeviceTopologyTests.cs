using RuinaoTesHardware;
using RuinaoTesProtocol.V14;
using Xunit;

namespace RuinaoHardwareEngineer.Tests;

public sealed class TesHardwareDeviceTopologyTests
{
    [Fact]
    public async Task ReadDeviceTopologyAsync_OnlyProbesSlotsReportedByBackplaneBitmap()
    {
        var transport = new TopologyTransport(slotBitmap: 0b0000_0100);
        await using var backplaneClient = new BackplaneClient(new EmptyDiscovery(), transport);
        var client = new TesHardwareDeviceClient(backplaneClient);

        var result = await client.ReadDeviceTopologyAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new byte[] { TesV14ProtocolConstants.BackplaneAddress, 0x02 }, transport.TargetAddresses);
        Assert.Equal(8, result.Slots.Count);
        Assert.False(result.Slots[0].IsInserted);
        Assert.True(result.Slots[2].IsInserted);
        Assert.True(result.Slots[2].IsOnline);
        Assert.Equal(TesBusinessBoardKind.Stimulation, result.Slots[2].BoardKind);
    }

    [Fact]
    public async Task ReadDeviceTopologyAsync_LimitsInsertedBoardProbeToFiveHundredMilliseconds()
    {
        var transport = new TopologyTransport(slotBitmap: 0b0000_0001);
        await using var backplaneClient = new BackplaneClient(new EmptyDiscovery(), transport);
        var client = new TesHardwareDeviceClient(backplaneClient);

        _ = await client.ReadDeviceTopologyAsync(TestContext.Current.CancellationToken);

        Assert.Equal(TimeSpan.FromMilliseconds(500), transport.BoardProbeTimeout);
    }

    private sealed class EmptyDiscovery : IUsbBackplaneDiscovery
    {
        public Task<UsbBackplaneDevice?> FindAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<UsbBackplaneDevice?>(null);
    }

    private sealed class TopologyTransport(uint slotBitmap) :
        IBackplaneTransport,
        IBackplaneRequestTimeoutTransport
    {
        private readonly List<byte> targetAddresses = [];

        public bool IsOpen => true;
        public IReadOnlyList<byte> TargetAddresses => targetAddresses;
        public TimeSpan? BoardProbeTimeout { get; private set; }

        public Task OpenAsync(
            UsbBackplaneDevice device,
            TimeSpan timeout,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<byte[]> ExchangeAsync(
            ReadOnlyMemory<byte> request,
            CancellationToken cancellationToken = default) =>
            BuildResponseAsync(request, cancellationToken);

        public Task<byte[]> ExchangeAsync(
            ReadOnlyMemory<byte> request,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            if (TryGetTargetAddress(request.Span) != TesV14ProtocolConstants.BackplaneAddress)
            {
                BoardProbeTimeout = timeout;
            }

            return BuildResponseAsync(request, cancellationToken);
        }

        public Task CloseAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private Task<byte[]> BuildResponseAsync(
            ReadOnlyMemory<byte> request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
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

            targetAddresses.Add(requestFrame.DestinationAddress);
            var responseRegisters = requestedRegisters
                .Select(register => new TesV14RegisterValue(
                    register.Address,
                    register.Address == 0x0900
                        ? slotBitmap
                        : register.Address == 0x0500
                            ? 0x74455300U // "tES\0"
                            : 0U))
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

        private static byte TryGetTargetAddress(ReadOnlySpan<byte> request)
        {
            Assert.True(
                TesV14ProtocolCodec.TryParseFrame(request, out var frame, out var error),
                error);
            Assert.NotNull(frame);
            return frame.DestinationAddress;
        }
    }
}
