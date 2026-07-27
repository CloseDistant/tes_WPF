namespace RuinaoSoftwareWpf.Tests;

using RuinaoTesHardware;
using RuinaoTesProtocol.V14;
using RuinaoTesProtocol.V15;
using Xunit;

public sealed class BackplaneStimulationUsbTest3CompatibilityTests
{
    private static readonly BackplaneConnectionOptions Options = new(
        TesV14ProtocolConstants.UsbTestProtocolVersion,
        TimeSpan.FromSeconds(5));

    [Fact]
    public async Task ConfigurePulseCurrent_MatchesEachWaveformAndControlResponse()
    {
        var transport = new ReplyingTransport();
        await using var client = new BackplaneClient(new EmptyDiscovery(), transport);
        var configuration = TesV15StimulationRegisterCodec.CreatePulseCurrent(
            channelNumber: 1,
            totalTimeMs: 120_000,
            lowLevel: 10_000,
            highLevel: 50_000,
            riseDurationUs: 5_000,
            plateauDurationUs: 10_000,
            intervalDurationUs: 20_000);

        var result = await client.ConfigureStimulationAsync(
            0x01,
            configuration,
            Options,
            TestContext.Current.CancellationToken);

        Assert.Equal(2, transport.ExchangeFrames.Count);
        Assert.Equal(1, transport.BatchCallCount);
        Assert.Single(result.WaveformWrites);
        Assert.All(
            result.WaveformWrites.Append(result.ControlWrite),
            operation =>
            {
                Assert.Equal(operation.RequestSequence, operation.ResponseAckSequence);
                Assert.Equal(0U, operation.ResponseStatusCode);
            });

        var firstWaveform = ParseRegisters(transport.ExchangeFrames[0]);
        var control = ParseRegisters(transport.ExchangeFrames[1]);

        Assert.Equal(0x01, ParseFrame(transport.ExchangeFrames[0]).DestinationAddress);
        Assert.Equal(0x3020, firstWaveform[0].Address);
        Assert.Equal(8U, firstWaveform[0].Value);
        Assert.Equal(new TesV14RegisterValue(0x302C, 571), firstWaveform[12]);
        Assert.Equal(TesV15StimulationRegisterCodec.EnableMaskRegister, control[0].Address);
        Assert.Equal(TesV15StimulationRegisterCodec.ConfigurationVersionRegister, control[1].Address);
    }

    [Fact]
    public async Task ConfigureDirectCurrent_MatchesTrapezoidAndControlResponses()
    {
        var transport = new ReplyingTransport();
        await using var client = new BackplaneClient(new EmptyDiscovery(), transport);
        var configuration = TesV15StimulationRegisterCodec.CreateDirectCurrent(
            channelNumber: 1,
            totalTimeMs: 120_000,
            lowLevel: 30_000,
            highLevel: 36_000,
            risePermille: 100,
            holdPermille: 800,
            fallPermille: 100);

        var result = await client.ConfigureStimulationAsync(
            0x01,
            configuration,
            Options,
            TestContext.Current.CancellationToken);

        Assert.Equal(2, transport.ExchangeFrames.Count);
        Assert.Equal(1, transport.BatchCallCount);
        Assert.Single(result.WaveformWrites);
        Assert.Equal(
            result.WaveformWrites[0].RequestSequence,
            result.WaveformWrites[0].ResponseAckSequence);
        Assert.Equal(result.ControlWrite.RequestSequence, result.ControlWrite.ResponseAckSequence);
        Assert.Equal(0U, result.WaveformWrites[0].ResponseStatusCode);
        Assert.Equal(0U, result.ControlWrite.ResponseStatusCode);
        Assert.Equal(8U, ParseRegisters(transport.ExchangeFrames[0])[0].Value);
        Assert.Equal(
            TesV15StimulationRegisterCodec.EnableMaskRegister,
            ParseRegisters(transport.ExchangeFrames[1])[0].Address);
    }

    [Fact]
    public async Task StartAndStop_RequireMatchingHardwareResponses()
    {
        var transport = new ReplyingTransport();
        await using var client = new BackplaneClient(new EmptyDiscovery(), transport);

        var start = await client.StartStimulationAsync(
            0x01,
            Options,
            TestContext.Current.CancellationToken);
        var stop = await client.StopStimulationAsync(
            0x01,
            Options,
            TestContext.Current.CancellationToken);

        Assert.Equal(2, transport.ExchangeFrames.Count);
        Assert.Equal(start.RequestSequence, start.ResponseAckSequence);
        Assert.Equal(stop.RequestSequence, stop.ResponseAckSequence);
        Assert.Equal(0U, start.ResponseStatusCode);
        Assert.Equal(0U, stop.ResponseStatusCode);
        Assert.Equal(
            new TesV14RegisterValue(TesV15StimulationRegisterCodec.StartRegister, 0),
            Assert.Single(ParseRegisters(transport.ExchangeFrames[0])));
        Assert.Equal(
            new TesV14RegisterValue(TesV15StimulationRegisterCodec.StopRegister, 0),
            Assert.Single(ParseRegisters(transport.ExchangeFrames[1])));
    }

    [Fact]
    public async Task Start_WhenResponseAcknowledgesAnotherSequence_Throws()
    {
        var transport = new ReplyingTransport(ackSequenceOffset: 1);
        await using var client = new BackplaneClient(new EmptyDiscovery(), transport);

        var exception = await Assert.ThrowsAsync<BackplaneConnectionException>(
            () => client.StartStimulationAsync(
                0x01,
                Options,
                TestContext.Current.CancellationToken));

        Assert.Contains("ACK序列不匹配", exception.Message);
    }

    [Fact]
    public async Task Start_WhenHardwareReturnsNonzeroStatus_Throws()
    {
        var transport = new ReplyingTransport(responseStatusCode: 2);
        await using var client = new BackplaneClient(new EmptyDiscovery(), transport);

        var exception = await Assert.ThrowsAsync<BackplaneConnectionException>(
            () => client.StartStimulationAsync(
                0x01,
                Options,
                TestContext.Current.CancellationToken));

        Assert.Contains("status=0x00000002", exception.Message);
    }

    private static TesV14Frame ParseFrame(byte[] bytes)
    {
        Assert.True(TesV14ProtocolCodec.TryParseFrame(bytes, out var frame, out var error), error);
        Assert.NotNull(frame);
        Assert.Equal(TesV14Command.Write, frame.Command);
        return frame;
    }

    private static IReadOnlyList<TesV14RegisterValue> ParseRegisters(byte[] bytes)
    {
        var frame = ParseFrame(bytes);
        Assert.True(
            TesV14RegisterPayloadCodec.TryDecode(frame.Payload, out var registers, out var error),
            error);
        return registers;
    }

    private sealed class EmptyDiscovery : IUsbBackplaneDiscovery
    {
        public Task<UsbBackplaneDevice?> FindAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<UsbBackplaneDevice?>(null);
    }

    private sealed class ReplyingTransport(
        int ackSequenceOffset = 0,
        uint responseStatusCode = 0) : IBackplaneTransport, IBackplaneBatchTransport
    {
        private ushort responseSequence = 100;

        public bool IsOpen => true;
        public List<byte[]> ExchangeFrames { get; } = [];
        public int BatchCallCount { get; private set; }

        public Task OpenAsync(
            UsbBackplaneDevice device,
            TimeSpan timeout,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<byte[]> ExchangeAsync(
            ReadOnlyMemory<byte> request,
            CancellationToken cancellationToken = default)
        {
            var requestBytes = request.ToArray();
            ExchangeFrames.Add(requestBytes);
            return Task.FromResult(BuildResponse(requestBytes));
        }

        public Task<IReadOnlyList<byte[]>> ExchangeBatchAsync(
            IReadOnlyList<ReadOnlyMemory<byte>> requests,
            CancellationToken cancellationToken = default)
        {
            BatchCallCount++;
            var requestBytes = requests.Select(request => request.ToArray()).ToArray();
            ExchangeFrames.AddRange(requestBytes);
            IReadOnlyList<byte[]> responses = requestBytes.Select(BuildResponse).ToArray();
            return Task.FromResult(responses);
        }

        private byte[] BuildResponse(byte[] requestBytes)
        {
            var requestFrame = ParseFrame(requestBytes);
            var ackSequence = unchecked((ushort)(requestFrame.SendSequence + ackSequenceOffset));
            Span<byte> responsePayload = stackalloc byte[TesV14OperationStatusCodec.PayloadLength];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(
                responsePayload,
                responseStatusCode);
            return TesV14ProtocolCodec.BuildFrame(
                TesV14FrameControl.None,
                TesV14Command.Response,
                requestFrame.DestinationAddress,
                TesV14ProtocolConstants.HostAddress,
                responseSequence++,
                ackSequence,
                responsePayload,
                requestFrame.Version);
        }

        public Task CloseAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
