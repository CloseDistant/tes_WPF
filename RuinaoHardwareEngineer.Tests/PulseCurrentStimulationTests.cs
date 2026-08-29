using RuinaoTesHardware;
using RuinaoTesProtocol.V14;
using Xunit;

namespace RuinaoHardwareEngineer.Tests;

public sealed class PulseCurrentStimulationTests
{
    [Fact]
    public void CreatePlan_TreatmentWindowDoesNotSubtractInitialRamp()
    {
        var plan = PulseCurrentStimulationClient.CreatePlan(
            CreateParameters() with
            {
                RampWidthMilliseconds = 500m,
                PulseWidthMilliseconds = 400m,
                IntervalWidthMilliseconds = 200m,
                TreatmentDurationSeconds = 1.0m,
            });

        Assert.Equal(2, plan.PlannedPulseCount);
        Assert.Equal(1_000m, plan.ScheduledPulseDurationMilliseconds);
        Assert.Equal(0m, plan.ZeroOutputTailMilliseconds);
        Assert.Equal(1_000U, plan.TreatmentDurationMilliseconds);
        Assert.Equal(1_500U, plan.TotalTimeMilliseconds);
    }

    [Fact]
    public void CreatePlan_DropsIncompleteFinalPulseAndKeepsZeroOutputTail()
    {
        var plan = PulseCurrentStimulationClient.CreatePlan(CreateParameters());

        Assert.Equal(40_000, plan.PlannedPulseCount);
        Assert.Equal(1_199_980m, plan.ScheduledPulseDurationMilliseconds);
        Assert.Equal(20m, plan.ZeroOutputTailMilliseconds);
        Assert.Equal(1_200_000U, plan.TreatmentDurationMilliseconds);
        Assert.Equal(1_200_005U, plan.TotalTimeMilliseconds);
        Assert.Equal(6U, plan.InitialRampSegment.WaveformType);
        Assert.Equal(5_000U, plan.InitialRampSegment.DurationMicroseconds);
        Assert.Equal(8U, plan.PulseTrainSegment.WaveformType);
        Assert.Equal(1_199_980_000U, plan.PulseTrainSegment.DurationMicroseconds);
        Assert.Equal(1U, plan.PulseTrainSegment.RiseMicroseconds);
        Assert.Equal(10_000U, plan.PulseTrainSegment.HighHoldMicroseconds);
        Assert.Equal(1U, plan.PulseTrainSegment.FallMicroseconds);
        Assert.Equal(20_000U, plan.PulseTrainSegment.LowHoldMicroseconds);
    }

    [Fact]
    public void CreatePlan_ReversedPolarityUsesNegativeTargetForBothSegments()
    {
        var plan = PulseCurrentStimulationClient.CreatePlan(
            CreateParameters() with
            {
                CurrentMilliampere = 10m,
                Polarity = PulseCurrentPolarity.Reversed,
            });

        Assert.Equal(-10m, plan.SignedCurrentMilliampere);
        Assert.Equal(-21_845, plan.InitialRampSegment.HighDa);
        Assert.Equal(-21_845, plan.PulseTrainSegment.HighDa);
        Assert.Equal(0, plan.InitialRampSegment.LowDa);
        Assert.Equal(0, plan.PulseTrainSegment.LowDa);
    }

    [Theory]
    [InlineData(0.001)]
    [InlineData(15.01)]
    public void CreatePlan_CurrentOutsideApprovedRange_Throws(double currentMilliampere)
    {
        var parameters = CreateParameters() with
        {
            CurrentMilliampere = (decimal)currentMilliampere,
        };

        Assert.Throws<ArgumentOutOfRangeException>(
            () => PulseCurrentStimulationClient.CreatePlan(parameters));
    }

    [Theory]
    [InlineData(-1, 10, 20)]
    [InlineData(1001, 10, 20)]
    [InlineData(1.5, 10, 20)]
    [InlineData(5, 0, 20)]
    [InlineData(5, 2001, 20)]
    [InlineData(5, 10, 0)]
    [InlineData(5, 10, 10001)]
    public void CreatePlan_InvalidIntegerMillisecondField_Throws(
        double rampWidthMilliseconds,
        double pulseWidthMilliseconds,
        double intervalWidthMilliseconds)
    {
        var parameters = CreateParameters() with
        {
            RampWidthMilliseconds = (decimal)rampWidthMilliseconds,
            PulseWidthMilliseconds = (decimal)pulseWidthMilliseconds,
            IntervalWidthMilliseconds = (decimal)intervalWidthMilliseconds,
        };

        Assert.Throws<ArgumentOutOfRangeException>(
            () => PulseCurrentStimulationClient.CreatePlan(parameters));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(3600.1)]
    [InlineData(1.01)]
    public void CreatePlan_InvalidTreatmentTime_Throws(double treatmentDurationSeconds)
    {
        var parameters = CreateParameters() with
        {
            TreatmentDurationSeconds = (decimal)treatmentDurationSeconds,
        };

        Assert.Throws<ArgumentOutOfRangeException>(
            () => PulseCurrentStimulationClient.CreatePlan(parameters));
    }

    [Fact]
    public async Task ConfigureAsync_WritesTypeSixThenTypeEightThenTwoWaveformControl()
    {
        var transport = new RecordingStatusTransport();
        await using var backplaneClient = new BackplaneClient(new EmptyDiscovery(), transport);
        var client = new PulseCurrentStimulationClient(backplaneClient);
        var parameters = CreateParameters() with { Channel = 3, CurrentMilliampere = 5m };

        var result = await client.ConfigureAsync(
            parameters,
            new BackplaneConnectionOptions(0x01, TimeSpan.FromSeconds(1)),
            TestContext.Current.CancellationToken);

        Assert.Equal(3, transport.Requests.Count);
        var ramp = DecodeRegisters(transport.Requests[0]);
        var pulse = DecodeRegisters(transport.Requests[1]);
        var control = DecodeRegisters(transport.Requests[2]);

        Assert.Equal(6U, ramp.Single(value => value.Address == 0x3420).Value);
        Assert.Equal(5_000U, ramp.Single(value => value.Address == 0x3421).Value);
        Assert.Equal(0U, ramp.Single(value => value.Address == 0x3427).Value);
        Assert.Equal(10_922U, ramp.Single(value => value.Address == 0x3428).Value);

        Assert.Equal(8U, pulse.Single(value => value.Address == 0x3430).Value);
        Assert.Equal(1_199_980_000U, pulse.Single(value => value.Address == 0x3431).Value);
        Assert.Equal(1U, pulse.Single(value => value.Address == 0x3439).Value);
        Assert.Equal(10_000U, pulse.Single(value => value.Address == 0x343A).Value);
        Assert.Equal(1U, pulse.Single(value => value.Address == 0x343B).Value);
        Assert.Equal(20_000U, pulse.Single(value => value.Address == 0x343C).Value);

        Assert.Equal(0x04U, control.Single(value => value.Address == 0x2E00).Value);
        Assert.Equal(2U, control.Single(value => value.Address == 0x3404).Value);
        Assert.Equal(1_200_005U, control.Single(value => value.Address == 0x3403).Value);
        Assert.Equal(
            StimulationHardwareConfirmationLevel.DeviceAccepted,
            result.ControlCommand.ConfirmationLevel);
    }

    [Fact]
    public async Task ConfigureAsync_SecondWaveformFailure_DoesNotWriteControl()
    {
        var transport = new RecordingStatusTransport(failOnRequestNumber: 2);
        await using var backplaneClient = new BackplaneClient(new EmptyDiscovery(), transport);
        var client = new PulseCurrentStimulationClient(backplaneClient);

        var exception = await Assert.ThrowsAsync<StimulationHardwareException>(() =>
            client.ConfigureAsync(
                CreateParameters(),
                new BackplaneConnectionOptions(0x01, TimeSpan.FromSeconds(1)),
                TestContext.Current.CancellationToken));

        Assert.Equal(StimulationHardwareDiagnosticCode.ResponseTimeout, exception.DiagnosticCode);
        Assert.Equal(2, transport.Requests.Count);
    }

    [Fact]
    public async Task StartAndStopChannel_UseSelectedChannelMask()
    {
        var transport = new RecordingStatusTransport();
        await using var backplaneClient = new BackplaneClient(new EmptyDiscovery(), transport);
        var client = new PulseCurrentStimulationClient(backplaneClient);
        var options = new BackplaneConnectionOptions(0x01, TimeSpan.FromSeconds(1));

        await client.StartChannelAsync(0x01, 3, options, TestContext.Current.CancellationToken);
        await client.StopChannelAsync(0x01, 3, options, TestContext.Current.CancellationToken);

        var start = Assert.Single(DecodeRegisters(transport.Requests[0]));
        var stop = Assert.Single(DecodeRegisters(transport.Requests[1]));
        Assert.Equal(0x0002, start.Address);
        Assert.Equal(0x04U, start.Value);
        Assert.Equal(0x0003, stop.Address);
        Assert.Equal(0x04U, stop.Value);
    }

    [Theory]
    [InlineData(0, 0, 0, false)]
    [InlineData(2.5, 1, 0, false)]
    [InlineData(5, 2, 0, false)]
    [InlineData(15, 0, 1, false)]
    [InlineData(1200005, 0, 40000, true)]
    public void Timeline_UsesRampPlusTreatmentTotal(
        double elapsedMilliseconds,
        double expectedCurrent,
        int expectedCompletedCount,
        bool expectedCompleted)
    {
        var plan = PulseCurrentStimulationClient.CreatePlan(
            CreateParameters() with { CurrentMilliampere = 2m });

        var progress = PulseCurrentStimulationTimeline.Calculate(
            plan,
            TimeSpan.FromMilliseconds(elapsedMilliseconds));

        Assert.Equal((decimal)expectedCurrent, progress.ExpectedCurrentMilliampere);
        Assert.Equal(expectedCompletedCount, progress.CompletedPulseCount);
        Assert.Equal(expectedCompleted, progress.IsCompleted);
    }

    private static PulseCurrentStimulationParameters CreateParameters() =>
        new(
            BoardAddress: 0x01,
            Channel: 1,
            CurrentMilliampere: 2m,
            RampWidthMilliseconds: 5m,
            PulseWidthMilliseconds: 10m,
            IntervalWidthMilliseconds: 20m,
            TreatmentDurationSeconds: 1_200m,
            PulseCurrentPolarity.Normal);

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

    private sealed class RecordingStatusTransport(int? failOnRequestNumber = null) : IBackplaneTransport
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
            if (Requests.Count == failOnRequestNumber)
            {
                throw new TimeoutException("测试超时");
            }

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
