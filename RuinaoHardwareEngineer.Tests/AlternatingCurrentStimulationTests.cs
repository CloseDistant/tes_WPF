using RuinaoTesHardware;
using RuinaoTesProtocol.V14;
using Xunit;

namespace RuinaoHardwareEngineer.Tests;

public sealed class AlternatingCurrentStimulationTests
{
    [Fact]
    public void Normalize_ZeroPeakCurrent_RestoresPreviousValidValue()
    {
        var result = AlternatingCurrentParameterRules.Normalize(
            AlternatingCurrentParameterKind.PeakCurrentMilliampere,
            "0",
            "0.125");

        Assert.Equal("0.125", result.Value);
        Assert.True(result.Adjusted);
        Assert.NotNull(result.Message);
    }

    [Fact]
    public void Normalize_ExcessPeakCurrent_UsesMaximumBoundary()
    {
        var result = AlternatingCurrentParameterRules.Normalize(
            AlternatingCurrentParameterKind.PeakCurrentMilliampere,
            "2.5",
            "0.125");

        Assert.Equal("2.000", result.Value);
        Assert.True(result.Adjusted);
        Assert.Contains("已调整", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Normalize_ExtraDecimals_RoundsWithoutWarning()
    {
        var result = AlternatingCurrentParameterRules.Normalize(
            AlternatingCurrentParameterKind.PeakCurrentMilliampere,
            "0.1235",
            "0.125");

        Assert.Equal("0.124", result.Value);
        Assert.True(result.Adjusted);
        Assert.Null(result.Message);
    }

    [Fact]
    public void CreatePlan_Defaults_GeneratesFiveStrictEqualTimeSegments()
    {
        var plan = AlternatingCurrentStimulationClient.CreatePlan(CreateParameters());

        Assert.Equal(5, plan.Segments.Count);
        Assert.Equal(
            [0.33m, 0.67m],
            plan.Segments.Take(2).Select(value => value.EnvelopeCoefficient));
        Assert.Equal(1m, plan.Segments[2].EnvelopeCoefficient);
        Assert.Equal(
            [0.67m, 0.33m],
            plan.Segments.Skip(3).Select(value => value.EnvelopeCoefficient));
        Assert.All(plan.Segments.Take(2), value => Assert.Equal(250_000U, value.DurationMicroseconds));
        Assert.Equal(1_199_000_000U, plan.Segments[2].DurationMicroseconds);
        Assert.All(plan.Segments.Skip(3), value => Assert.Equal(250_000U, value.DurationMicroseconds));
        Assert.Equal(1_200_000_000L, plan.Segments.Sum(value => (long)value.DurationMicroseconds));
    }

    [Fact]
    public void CreatePlan_TwoMilliampere_UsesSingleSidedPeakDaValues()
    {
        var plan = AlternatingCurrentStimulationClient.CreatePlan(
            CreateParameters() with { PeakCurrentMilliampere = 2.000m });

        Assert.Equal(1_442U, plan.Segments[0].AmplitudeDa);
        Assert.Equal(2_927U, plan.Segments[1].AmplitudeDa);
        Assert.Equal(4_369U, plan.Segments[2].AmplitudeDa);
        Assert.Equal(1_442U, plan.Segments[^1].AmplitudeDa);
    }

    [Fact]
    public void CreatePlan_TracksCumulativePhaseForEachStrictTimeBoundary()
    {
        var plan = AlternatingCurrentStimulationClient.CreatePlan(
            CreateParameters() with { FrequencyHz = 1_001 });

        Assert.Equal([0U, 90U], plan.Segments.Take(2).Select(value => value.PhaseDegree));
    }

    [Fact]
    public void CreatePlan_ZeroStableDuration_IsAllowedAndOmitsStableSegment()
    {
        var plan = AlternatingCurrentStimulationClient.CreatePlan(
            CreateParameters() with
            {
                RampUpSeconds = 0.5m,
                RampDownSeconds = 0.5m,
                TotalDurationSeconds = 1.0m,
            });

        Assert.Equal(4, plan.Segments.Count);
        Assert.DoesNotContain(plan.Segments, value => value.Stage == AlternatingCurrentWaveformStage.Stable);
    }

    [Fact]
    public void CreatePlan_RampsLongerThanTotalDuration_Throws()
    {
        var parameters = CreateParameters() with
        {
            RampUpSeconds = 0.6m,
            RampDownSeconds = 0.5m,
            TotalDurationSeconds = 1.0m,
        };

        var exception = Assert.Throws<ArgumentException>(
            () => AlternatingCurrentStimulationClient.CreatePlan(parameters));

        Assert.Contains("不能小于", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Timeline_UsesSineCarrierAndCurrentStepEnvelope()
    {
        var plan = AlternatingCurrentStimulationClient.CreatePlan(
            CreateParameters() with
            {
                PeakCurrentMilliampere = 1.000m,
                FrequencyHz = 10,
            });

        var progress = AlternatingCurrentStimulationTimeline.Calculate(
            plan,
            TimeSpan.FromSeconds(0.025));

        Assert.Equal(0.330m, progress.EnvelopePeakMilliampere);
        Assert.Equal(0.330m, progress.SimulatedCurrentMilliampere);
        Assert.Equal(1, progress.SegmentIndex);
        Assert.Equal(AlternatingCurrentWaveformStage.RampUp, progress.Stage);
    }

    [Fact]
    public async Task ConfigureAsync_WritesEveryTypeTwoSegmentThenControl()
    {
        var transport = new RecordingStatusTransport();
        await using var backplaneClient = new BackplaneClient(new EmptyDiscovery(), transport);
        var client = new AlternatingCurrentStimulationClient(backplaneClient);

        var result = await client.ConfigureAsync(
            CreateParameters(),
            new BackplaneConnectionOptions(0x01, TimeSpan.FromSeconds(1)),
            TestContext.Current.CancellationToken);

        Assert.Equal(6, transport.Requests.Count);
        Assert.Equal(5, result.WaveformCommands.Count);
        var firstWaveform = DecodeRegisters(transport.Requests[0]);
        var lastWaveform = DecodeRegisters(transport.Requests[4]);
        var control = DecodeRegisters(transport.Requests[5]);
        Assert.Equal(0x3020, firstWaveform[0].Address);
        Assert.Equal(2U, firstWaveform[0].Value);
        Assert.Equal(1_000U, firstWaveform[2].Value);
        Assert.Equal(0x3060, lastWaveform[0].Address);
        Assert.Equal(7U, lastWaveform[3].Value);
        Assert.Equal(1_200_000U, control.Single(value => value.Address == 0x3003).Value);
        Assert.Equal(5U, control.Single(value => value.Address == 0x3004).Value);
    }

    private static AlternatingCurrentStimulationParameters CreateParameters() =>
        new(
            BoardAddress: 0x01,
            Channel: 1,
            PeakCurrentMilliampere: 0.010m,
            RampUpSeconds: 0.5m,
            RampDownSeconds: 0.5m,
            FrequencyHz: 1_000,
            TotalDurationSeconds: 1_200m);

    private static IReadOnlyList<TesV14RegisterValue> DecodeRegisters(byte[] request)
    {
        Assert.True(TesV14ProtocolCodec.TryParseFrame(request, out var frame, out var frameError), frameError);
        Assert.NotNull(frame);
        Assert.True(
            TesV14RegisterPayloadCodec.TryDecode(frame.Payload, out var registers, out var payloadError),
            payloadError);
        return registers;
    }

    private sealed class EmptyDiscovery : IUsbBackplaneDiscovery
    {
        public Task<UsbBackplaneDevice?> FindAsync(CancellationToken cancellationToken = default) =>
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
            Assert.True(TesV14ProtocolCodec.TryParseFrame(request.Span, out var frame, out var error), error);
            Assert.NotNull(frame);
            return Task.FromResult(TesV14ProtocolCodec.BuildFrame(
                TesV14FrameControl.None,
                TesV14Command.Response,
                frame.DestinationAddress,
                frame.SourceAddress,
                27,
                frame.SendSequence,
                [0, 0, 0, 0]));
        }

        public Task CloseAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public ValueTask DisposeAsync() =>
            ValueTask.CompletedTask;
    }
}
