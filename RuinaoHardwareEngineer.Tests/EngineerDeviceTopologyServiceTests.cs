using RuinaoHardwareEngineer.Features.DeviceTopology;
using RuinaoTesHardware;
using RuinaoTesProtocol.V14;
using Xunit;

namespace RuinaoHardwareEngineer.Tests;

public sealed class EngineerDeviceTopologyServiceTests
{
    [Fact]
    public void CreateProbeOptions_WhenNormalTimeoutIsFiveSeconds_UsesFiveHundredMilliseconds()
    {
        var normalOptions = new BackplaneConnectionOptions(
            0x01,
            TimeSpan.FromSeconds(5),
            HandshakeAckRequired: true);

        var probeOptions = EngineerDeviceTopologyService.CreateProbeOptions(normalOptions);

        Assert.Equal(TimeSpan.FromMilliseconds(500), probeOptions.Timeout);
        Assert.Equal(normalOptions.ProtocolVersion, probeOptions.ProtocolVersion);
        Assert.Equal(normalOptions.HandshakeAckRequired, probeOptions.HandshakeAckRequired);
    }

    [Fact]
    public void CreateProbeOptions_WhenCallerAlreadyUsesShorterTimeout_PreservesIt()
    {
        var normalOptions = new BackplaneConnectionOptions(
            0x01,
            TimeSpan.FromMilliseconds(300));

        var probeOptions = EngineerDeviceTopologyService.CreateProbeOptions(normalOptions);

        Assert.Equal(TimeSpan.FromMilliseconds(300), probeOptions.Timeout);
    }

    [Fact]
    public async Task ReadRegisters_WhenTransportSupportsPerRequestTimeout_ForwardsProbeTimeout()
    {
        var transport = new TimeoutRecordingTransport();
        await using var client = new BackplaneClient(new EmptyDiscovery(), transport);
        var probeOptions = EngineerDeviceTopologyService.CreateProbeOptions(
            new BackplaneConnectionOptions(0x01, TimeSpan.FromSeconds(5)));

        await client.ReadRegistersAsync(
            0x01,
            [0x0500],
            probeOptions,
            TestContext.Current.CancellationToken);

        Assert.Equal(TimeSpan.FromMilliseconds(500), transport.LastRequestTimeout);
    }

    [Fact]
    public async Task ScanAsync_UsesBackplaneBitmapAndOnlyReadsInsertedBoardAddresses()
    {
        var transport = new SlotBitmapTransport(slotBitmap: 0x00000002);
        await using var client = new BackplaneClient(new EmptyDiscovery(), transport);
        var service = new EngineerDeviceTopologyService(client);

        var result = await service.ScanAsync(
            new BackplaneConnectionOptions(0x01, TimeSpan.FromSeconds(5)),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(
            new byte[] { TesV14ProtocolConstants.BackplaneAddress, 0x01 },
            transport.TargetAddresses);
        Assert.False(result[0].IsInserted);
        Assert.True(result[1].IsInserted);
        Assert.True(result[1].IsOnline);
        Assert.Equal(EngineerBoardKind.Stimulation, result[1].BoardKind);
        Assert.All(result.Skip(2), slot => Assert.False(slot.IsInserted));
    }

    private sealed class EmptyDiscovery : IUsbBackplaneDiscovery
    {
        public Task<UsbBackplaneDevice?> FindAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<UsbBackplaneDevice?>(null);
    }

    private sealed class TimeoutRecordingTransport :
        IBackplaneTransport,
        IBackplaneRequestTimeoutTransport
    {
        public bool IsOpen => true;
        public TimeSpan? LastRequestTimeout { get; private set; }

        public Task OpenAsync(
            UsbBackplaneDevice device,
            TimeSpan timeout,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<byte[]> ExchangeAsync(
            ReadOnlyMemory<byte> request,
            CancellationToken cancellationToken = default) =>
            ExchangeAsync(request, TimeSpan.FromSeconds(5), cancellationToken);

        public Task<byte[]> ExchangeAsync(
            ReadOnlyMemory<byte> request,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            LastRequestTimeout = timeout;
            Assert.True(
                TesV14ProtocolCodec.TryParseFrame(request.Span, out var requestFrame, out var error),
                error);
            Assert.NotNull(requestFrame);
            var payload = TesV14RegisterPayloadCodec.Encode(
                [new TesV14RegisterValue(0x0500, 1)]);
            return Task.FromResult(TesV14ProtocolCodec.BuildFrame(
                TesV14FrameControl.None,
                TesV14Command.Response,
                requestFrame.DestinationAddress,
                requestFrame.SourceAddress,
                27,
                requestFrame.SendSequence,
                payload,
                requestFrame.Version));
        }

        public Task CloseAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public ValueTask DisposeAsync() =>
            ValueTask.CompletedTask;
    }

    private sealed class SlotBitmapTransport(uint slotBitmap) : IBackplaneTransport
    {
        private readonly List<byte> targetAddresses = new();

        public bool IsOpen => true;
        public IReadOnlyList<byte> TargetAddresses => targetAddresses;

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

            targetAddresses.Add(requestFrame.DestinationAddress);
            var responseRegisters = requestedRegisters
                .Select(register => new TesV14RegisterValue(
                    register.Address,
                    register.Address == 0x0900 ? slotBitmap : 0U))
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
