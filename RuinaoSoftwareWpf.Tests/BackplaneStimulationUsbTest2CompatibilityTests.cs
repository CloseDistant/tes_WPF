namespace RuinaoSoftwareWpf.Tests;

using RuinaoTesHardware;
using RuinaoTesProtocol.V14;
using RuinaoTesProtocol.V15;
using Xunit;

public sealed class BackplaneStimulationUsbTest2CompatibilityTests
{
    private static readonly BackplaneConnectionOptions Options = new(
        TesV14ProtocolConstants.UsbTestProtocolVersion,
        TimeSpan.FromSeconds(5));

    [Fact]
    public async Task ConfigurePulseCurrent_SendsEachWaveformThenControlWithoutWaitingForReply()
    {
        var transport = new RecordingOneWayTransport();
        await using var client = new BackplaneClient(new EmptyDiscovery(), transport);
        var configuration = TesV15StimulationRegisterCodec.CreatePulseCurrent(
            channelNumber: 1,
            totalTimeMs: 120_000,
            baselineLevel: 30_000,
            targetLevel: 36_000,
            riseDurationUs: 5_000,
            plateauDurationUs: 10_000,
            intervalDurationUs: 20_000);

        var result = await client.ConfigureStimulationAsync(
            0x01,
            configuration,
            Options,
            TestContext.Current.CancellationToken);

        Assert.Equal(3, transport.SentFrames.Count);
        Assert.Equal(2, result.WaveformWrites.Count);
        Assert.Empty(transport.ExchangeFrames);

        var firstWaveform = ParseRegisters(transport.SentFrames[0]);
        var intervalWaveform = ParseRegisters(transport.SentFrames[1]);
        var control = ParseRegisters(transport.SentFrames[2]);

        Assert.Equal(0x01, ParseFrame(transport.SentFrames[0]).DestinationAddress);
        Assert.Equal(0x3020, firstWaveform[0].Address);
        Assert.Equal(8U, firstWaveform[0].Value);
        Assert.Equal(0x3030, intervalWaveform[0].Address);
        Assert.Equal(1U, intervalWaveform[0].Value);
        Assert.Equal(TesV15StimulationRegisterCodec.EnableMaskRegister, control[0].Address);
        Assert.Equal(TesV15StimulationRegisterCodec.ConfigurationVersionRegister, control[1].Address);
    }

    [Fact]
    public async Task ConfigureDirectCurrent_SendsTrapezoidThenControlWithoutWaitingForReply()
    {
        var transport = new RecordingOneWayTransport();
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

        Assert.Equal(2, transport.SentFrames.Count);
        Assert.Single(result.WaveformWrites);
        Assert.Empty(transport.ExchangeFrames);
        Assert.Equal(8U, ParseRegisters(transport.SentFrames[0])[0].Value);
        Assert.Equal(
            TesV15StimulationRegisterCodec.EnableMaskRegister,
            ParseRegisters(transport.SentFrames[1])[0].Address);
    }

    [Fact]
    public async Task StartAndStop_SendUsbTest2CommandRegistersWithoutExchange()
    {
        var transport = new RecordingOneWayTransport();
        await using var client = new BackplaneClient(new EmptyDiscovery(), transport);

        await client.StartStimulationAsync(
            0x01,
            Options,
            TestContext.Current.CancellationToken);
        await client.StopStimulationAsync(
            0x01,
            Options,
            TestContext.Current.CancellationToken);

        Assert.Equal(2, transport.SentFrames.Count);
        Assert.Empty(transport.ExchangeFrames);
        Assert.Equal(
            new TesV14RegisterValue(TesV15StimulationRegisterCodec.StartRegister, 0),
            Assert.Single(ParseRegisters(transport.SentFrames[0])));
        Assert.Equal(
            new TesV14RegisterValue(TesV15StimulationRegisterCodec.StopRegister, 0),
            Assert.Single(ParseRegisters(transport.SentFrames[1])));
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

    private sealed class RecordingOneWayTransport : IBackplaneTransport, IBackplaneOneWayTransport
    {
        public bool IsOpen => true;
        public List<byte[]> SentFrames { get; } = [];
        public List<byte[]> ExchangeFrames { get; } = [];

        public Task OpenAsync(
            UsbBackplaneDevice device,
            TimeSpan timeout,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task SendAsync(
            ReadOnlyMemory<byte> request,
            CancellationToken cancellationToken = default)
        {
            SentFrames.Add(request.ToArray());
            return Task.CompletedTask;
        }

        public Task<byte[]> ExchangeAsync(
            ReadOnlyMemory<byte> request,
            CancellationToken cancellationToken = default)
        {
            ExchangeFrames.Add(request.ToArray());
            throw new InvalidOperationException("刺激写命令不应进入请求—应答交换。");
        }

        public Task CloseAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
