using RuinaoTesHardware;
using RuinaoTesProtocol.V14;
using Xunit;

namespace RuinaoHardwareEngineer.Tests;

public sealed class MonophasicPulseCurrentStimulationTests
{
    [Fact]
    public void CreatePlan_UsesPositiveTriangleWithoutHighHold()
    {
        var plan = MonophasicPulseCurrentStimulationClient.CreatePlan(
            CreateParameters() with
            {
                CurrentMilliampere = 5.00m,
                RampUpDownSeconds = 0.5m,
                IntervalSeconds = 0m,
                TotalDurationSeconds = 120m,
            });

        Assert.Equal(1.0m, plan.SinglePulseDurationSeconds);
        Assert.Equal(120, plan.PlannedPulseCount);
        Assert.Equal(120m, plan.ScheduledWaveformDurationSeconds);
        Assert.Equal(0m, plan.ZeroOutputTailSeconds);
        Assert.Equal(0, plan.LowDa);
        Assert.Equal(10_922, plan.HighDa);
        Assert.Equal(500_000U, plan.RiseMicroseconds);
        Assert.Equal(0U, plan.HighHoldMicroseconds);
        Assert.Equal(500_000U, plan.FallMicroseconds);
        Assert.Equal(0U, plan.LowHoldMicroseconds);
    }

    [Fact]
    public void CreatePlan_DropsIncompleteFinalPulseAndKeepsZeroOutputTail()
    {
        var plan = MonophasicPulseCurrentStimulationClient.CreatePlan(
            CreateParameters() with
            {
                RampUpDownSeconds = 1m,
                IntervalSeconds = 3m,
                TotalDurationSeconds = 20m,
            });

        Assert.Equal(2m, plan.SinglePulseDurationSeconds);
        Assert.Equal(5m, plan.CycleDurationSeconds);
        Assert.Equal(4, plan.PlannedPulseCount);
        Assert.Equal(17m, plan.ScheduledWaveformDurationSeconds);
        Assert.Equal(3m, plan.ZeroOutputTailSeconds);
        Assert.Equal(17_000_000U, plan.DurationMicroseconds);
        Assert.Equal(20_000U, plan.TotalTimeMilliseconds);
    }

    [Fact]
    public void CreatePlan_OnePulse_DoesNotAppendTrailingInterval()
    {
        var plan = MonophasicPulseCurrentStimulationClient.CreatePlan(
            CreateParameters() with
            {
                RampUpDownSeconds = 1m,
                IntervalSeconds = 100m,
                TotalDurationSeconds = 2m,
            });

        Assert.Equal(1, plan.PlannedPulseCount);
        Assert.Equal(2m, plan.ScheduledWaveformDurationSeconds);
        Assert.Equal(0m, plan.ZeroOutputTailSeconds);
    }

    [Theory]
    [InlineData(0.001)]
    [InlineData(15.01)]
    public void CreatePlan_InvalidCurrent_Throws(double currentMilliampere)
    {
        var parameters = CreateParameters() with
        {
            CurrentMilliampere = (decimal)currentMilliampere,
        };

        Assert.Throws<ArgumentOutOfRangeException>(
            () => MonophasicPulseCurrentStimulationClient.CreatePlan(parameters));
    }

    [Fact]
    public void CreatePlan_TotalShorterThanTriangle_Throws()
    {
        var parameters = CreateParameters() with
        {
            RampUpDownSeconds = 10m,
            TotalDurationSeconds = 19.9m,
        };

        Assert.Throws<ArgumentException>(
            () => MonophasicPulseCurrentStimulationClient.CreatePlan(parameters));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(100.1)]
    [InlineData(0.11)]
    public void CreatePlan_InvalidRamp_Throws(double rampSeconds)
    {
        var parameters = CreateParameters() with
        {
            RampUpDownSeconds = (decimal)rampSeconds,
        };

        Assert.Throws<ArgumentOutOfRangeException>(
            () => MonophasicPulseCurrentStimulationClient.CreatePlan(parameters));
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(3600.1)]
    [InlineData(0.01)]
    public void CreatePlan_InvalidInterval_Throws(double intervalSeconds)
    {
        var parameters = CreateParameters() with
        {
            IntervalSeconds = (decimal)intervalSeconds,
        };

        Assert.Throws<ArgumentOutOfRangeException>(
            () => MonophasicPulseCurrentStimulationClient.CreatePlan(parameters));
    }

    [Fact]
    public void CreatePlan_AcceptsApprovedMaximumTimeBoundaries()
    {
        var plan = MonophasicPulseCurrentStimulationClient.CreatePlan(
            CreateParameters() with
            {
                RampUpDownSeconds = 100m,
                IntervalSeconds = 3_600m,
                TotalDurationSeconds = 3_600m,
            });

        Assert.Equal(1, plan.PlannedPulseCount);
        Assert.Equal(200m, plan.ScheduledWaveformDurationSeconds);
        Assert.Equal(3_400m, plan.ZeroOutputTailSeconds);
    }

    [Fact]
    public async Task ConfigureAsync_WritesSharedTypeEightLayoutWithZeroHighHold()
    {
        var transport = new RecordingStatusTransport();
        await using var backplaneClient = new BackplaneClient(new EmptyDiscovery(), transport);
        var client = new MonophasicPulseCurrentStimulationClient(backplaneClient);
        var parameters = CreateParameters() with
        {
            Channel = 3,
            CurrentMilliampere = 5m,
            RampUpDownSeconds = 0.5m,
            IntervalSeconds = 1m,
            TotalDurationSeconds = 10m,
        };

        var result = await client.ConfigureAsync(
            parameters,
            new BackplaneConnectionOptions(0x01, TimeSpan.FromSeconds(1)),
            TestContext.Current.CancellationToken);

        Assert.Equal(2, transport.Requests.Count);
        var waveform = DecodeRegisters(transport.Requests[0]);
        var control = DecodeRegisters(transport.Requests[1]);
        Assert.Equal(8U, waveform.Single(value => value.Address == 0x3420).Value);
        Assert.Equal(9_000_000U, waveform.Single(value => value.Address == 0x3421).Value);
        Assert.Equal(0U, waveform.Single(value => value.Address == 0x3427).Value);
        Assert.Equal(10_922U, waveform.Single(value => value.Address == 0x3428).Value);
        Assert.Equal(500_000U, waveform.Single(value => value.Address == 0x3429).Value);
        Assert.Equal(0U, waveform.Single(value => value.Address == 0x342A).Value);
        Assert.Equal(500_000U, waveform.Single(value => value.Address == 0x342B).Value);
        Assert.Equal(1_000_000U, waveform.Single(value => value.Address == 0x342C).Value);
        Assert.Equal(10_000U, control.Single(value => value.Address == 0x3403).Value);
        Assert.Equal(
            StimulationHardwareConfirmationLevel.DeviceAccepted,
            result.WaveformCommand.ConfirmationLevel);
    }

    [Fact]
    public async Task StartAndStopChannel_UseSameBusinessBoardCommandPathAsTdcs()
    {
        var transport = new RecordingStatusTransport();
        await using var backplaneClient = new BackplaneClient(new EmptyDiscovery(), transport);
        var client = new MonophasicPulseCurrentStimulationClient(backplaneClient);
        var options = new BackplaneConnectionOptions(0x01, TimeSpan.FromSeconds(1));

        await client.StartChannelAsync(
            0x01,
            3,
            options,
            TestContext.Current.CancellationToken);
        await client.StopChannelAsync(
            0x01,
            3,
            options,
            TestContext.Current.CancellationToken);

        Assert.Equal(2, transport.Requests.Count);
        var start = Assert.Single(DecodeRegisters(transport.Requests[0]));
        var stop = Assert.Single(DecodeRegisters(transport.Requests[1]));
        Assert.Equal(0x0002, start.Address);
        Assert.Equal(0x04U, start.Value);
        Assert.Equal(0x0003, stop.Address);
        Assert.Equal(0x04U, stop.Value);
    }

    [Fact]
    public async Task EmergencyStop_UsesBackplaneStopRegisterWithZeroValue()
    {
        var transport = new RecordingStatusTransport();
        await using var backplaneClient = new BackplaneClient(new EmptyDiscovery(), transport);
        var client = new MonophasicPulseCurrentStimulationClient(backplaneClient);

        await client.EmergencyStopBackplaneAsync(
            new BackplaneConnectionOptions(0x01, TimeSpan.FromSeconds(1)),
            TestContext.Current.CancellationToken);

        var request = Assert.Single(transport.Requests);
        Assert.True(
            TesV14ProtocolCodec.TryParseFrame(request, out var frame, out var error),
            error);
        Assert.NotNull(frame);
        Assert.Equal(TesV14ProtocolConstants.BackplaneAddress, frame.DestinationAddress);
        var register = Assert.Single(DecodeRegisters(request));
        Assert.Equal(0x0003, register.Address);
        Assert.Equal(0U, register.Value);
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(0.5, 1, 0)]
    [InlineData(1, 2, 0)]
    [InlineData(1.5, 1, 0)]
    [InlineData(2, 0, 1)]
    [InlineData(4.5, 1, 1)]
    [InlineData(10.5, 0, 3)]
    [InlineData(12, 0, 3)]
    public void Timeline_CalculatesTriangleIntervalAndZeroTail(
        double elapsedSeconds,
        double expectedCurrent,
        int expectedCompletedCount)
    {
        var plan = MonophasicPulseCurrentStimulationClient.CreatePlan(
            CreateParameters() with
            {
                CurrentMilliampere = 2m,
                RampUpDownSeconds = 1m,
                IntervalSeconds = 2m,
                TotalDurationSeconds = 12m,
            });

        var progress = MonophasicPulseCurrentStimulationTimeline.Calculate(
            plan,
            TimeSpan.FromSeconds(elapsedSeconds));

        Assert.Equal((decimal)expectedCurrent, progress.ExpectedCurrentMilliampere);
        Assert.Equal(expectedCompletedCount, progress.CompletedPulseCount);
        Assert.Equal(elapsedSeconds >= 12, progress.IsCompleted);
    }

    private static MonophasicPulseCurrentStimulationParameters CreateParameters() =>
        new(
            BoardAddress: 0x01,
            Channel: 1,
            CurrentMilliampere: 2m,
            RampUpDownSeconds: 0.5m,
            IntervalSeconds: 0m,
            TotalDurationSeconds: 120m);

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
            Assert.True(TesV14ProtocolCodec.TryParseFrame(request.Span, out var frame, out var error), error);
            Assert.NotNull(frame);
            var response = TesV14ProtocolCodec.BuildFrame(
                TesV14FrameControl.None,
                TesV14Command.Response,
                frame.DestinationAddress,
                frame.SourceAddress,
                27,
                frame.SendSequence,
                [0, 0, 0, 0]);
            return Task.FromResult(response);
        }

        public Task CloseAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public ValueTask DisposeAsync() =>
            ValueTask.CompletedTask;
    }
}
