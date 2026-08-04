using RuinaoTesHardware;
using RuinaoTesProtocol.V14;
using Xunit;

namespace RuinaoHardwareEngineer.Tests;

public sealed class ProductDirectCurrentPlanFactoryTests
{
    [Fact]
    public void ConvertCurrentToDa_FiveMilliampere_UsesFixedFifteenMilliampereCalibration()
    {
        Assert.Equal(10_922, DirectCurrentStimulationClient.ConvertCurrentToDa(5.00m));
    }

    [Fact]
    public void Create_ContinuousNormal_GeneratesSinglePositiveTypeEightWaveform()
    {
        var parameters = CreateParameters(
            DirectCurrentDeliveryMode.Continuous,
            DirectCurrentPolarity.Normal) with
        {
            CurrentMilliampere = 5.00m,
            RampUpSeconds = 0.5m,
            RampDownSeconds = 0.5m,
            TotalDurationSeconds = 120m,
        };

        var plan = DirectCurrentStimulationClient.CreatePlan(parameters);

        Assert.Equal(8U, plan.WaveformType);
        Assert.Equal(120_000_000U, plan.DurationMicroseconds);
        Assert.Equal(0, plan.LowDa);
        Assert.Equal(10_922, plan.HighDa);
        Assert.Equal(500_000U, plan.RiseMicroseconds);
        Assert.Equal(119_000_000U, plan.HighHoldMicroseconds);
        Assert.Equal(500_000U, plan.FallMicroseconds);
        Assert.Equal(0U, plan.LowHoldMicroseconds);
        Assert.Equal(120_000U, plan.TotalTimeMilliseconds);
    }

    [Fact]
    public void Create_ContinuousReversed_GeneratesZeroLowAndNegativeHigh()
    {
        var parameters = CreateParameters(
            DirectCurrentDeliveryMode.Continuous,
            DirectCurrentPolarity.Reversed) with
        {
            CurrentMilliampere = 10.00m,
        };

        var plan = DirectCurrentStimulationClient.CreatePlan(parameters);

        Assert.Equal(0, plan.LowDa);
        Assert.Equal(-21_845, plan.HighDa);
    }

    [Fact]
    public void Create_Intermittent_MapsSingleDurationAndIntervalToTypeEightTimings()
    {
        var parameters = CreateParameters(
            DirectCurrentDeliveryMode.Intermittent,
            DirectCurrentPolarity.Normal) with
        {
            RampUpSeconds = 10m,
            RampDownSeconds = 10m,
            TotalDurationSeconds = 120m,
            SingleDurationSeconds = 30m,
            IntervalSeconds = 5m,
        };

        var plan = DirectCurrentStimulationClient.CreatePlan(parameters);

        Assert.Equal(120_000_000U, plan.DurationMicroseconds);
        Assert.Equal(10_000_000U, plan.RiseMicroseconds);
        Assert.Equal(10_000_000U, plan.HighHoldMicroseconds);
        Assert.Equal(10_000_000U, plan.FallMicroseconds);
        Assert.Equal(5_000_000U, plan.LowHoldMicroseconds);
    }

    [Fact]
    public void Create_UsesChannelMaskAndUsbTestFourControlDefaults()
    {
        var parameters = CreateParameters(
            DirectCurrentDeliveryMode.Continuous,
            DirectCurrentPolarity.Normal) with
        {
            Channel = 3,
        };

        var plan = DirectCurrentStimulationClient.CreatePlan(parameters);

        Assert.Equal(0x04U, plan.EnableMask);
        Assert.Equal(0x16U, plan.ConfigurationVersion);
    }

    [Fact]
    public void Create_IntermittentSingleDurationNotLongerThanRamps_Throws()
    {
        var parameters = CreateParameters(
            DirectCurrentDeliveryMode.Intermittent,
            DirectCurrentPolarity.Normal) with
        {
            RampUpSeconds = 10m,
            RampDownSeconds = 10m,
            SingleDurationSeconds = 20m,
        };

        var exception = Assert.Throws<ArgumentException>(
            () => DirectCurrentStimulationClient.CreatePlan(parameters));

        Assert.Contains("单次时长", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConfigureAsync_SendsUsbTestFourWaveformThenControlRegisters()
    {
        var transport = new RecordingStatusTransport();
        var backplaneClient = new BackplaneClient(new EmptyDiscovery(), transport);
        var client = new DirectCurrentStimulationClient(backplaneClient);
        var parameters = CreateParameters(
            DirectCurrentDeliveryMode.Continuous,
            DirectCurrentPolarity.Normal);

        var result = await client.ConfigureAsync(
            parameters,
            new BackplaneConnectionOptions(0x01, TimeSpan.FromSeconds(1)),
            TestContext.Current.CancellationToken);

        Assert.Equal(2, transport.Requests.Count);
        var waveformRegisters = DecodeRegisters(transport.Requests[0]);
        var controlRegisters = DecodeRegisters(transport.Requests[1]);
        Assert.Equal(Enumerable.Range(0x3020, 16), waveformRegisters.Select(value => (int)value.Address));
        Assert.Equal(
            [0x2E00, 0x2E01, 0x3000, 0x3001, 0x3002, 0x3003, 0x3004, 0x3005],
            controlRegisters.Select(value => (int)value.Address));
        Assert.Equal(DirectCurrentConfirmationLevel.DeviceAccepted, result.WaveformCommand.ConfirmationLevel);
        Assert.Equal(DirectCurrentConfirmationLevel.DeviceAccepted, result.ControlCommand.ConfirmationLevel);
    }

    [Fact]
    public async Task ConfigureAsync_Reversed_EncodesNegativeHighDaAsSignedRegisterValue()
    {
        var transport = new RecordingStatusTransport();
        var backplaneClient = new BackplaneClient(new EmptyDiscovery(), transport);
        var client = new DirectCurrentStimulationClient(backplaneClient);
        var parameters = CreateParameters(
            DirectCurrentDeliveryMode.Continuous,
            DirectCurrentPolarity.Reversed) with
        {
            CurrentMilliampere = 10.00m,
        };

        await client.ConfigureAsync(
            parameters,
            new BackplaneConnectionOptions(0x01, TimeSpan.FromSeconds(1)),
            TestContext.Current.CancellationToken);

        var waveformRegisters = DecodeRegisters(transport.Requests[0]);
        Assert.Equal(0U, waveformRegisters.Single(value => value.Address == 0x3027).Value);
        Assert.Equal(
            unchecked((uint)-21_845),
            waveformRegisters.Single(value => value.Address == 0x3028).Value);
    }

    [Fact]
    public async Task StartChannelAsync_ChannelThree_WritesSingleBitMaskToStartRegister()
    {
        var transport = new RecordingStatusTransport();
        await using var backplaneClient = new BackplaneClient(new EmptyDiscovery(), transport);
        var client = new DirectCurrentStimulationClient(backplaneClient);

        await client.StartChannelAsync(
            0x01,
            3,
            new BackplaneConnectionOptions(0x01, TimeSpan.FromSeconds(1)),
            TestContext.Current.CancellationToken);

        var register = Assert.Single(DecodeRegisters(Assert.Single(transport.Requests)));
        Assert.Equal(0x0002, register.Address);
        Assert.Equal(0x00000004U, register.Value);
    }

    [Fact]
    public async Task StopChannelsAsync_MultipleChannels_WritesMaskToStopRegister()
    {
        var transport = new RecordingStatusTransport();
        await using var backplaneClient = new BackplaneClient(new EmptyDiscovery(), transport);
        var client = new DirectCurrentStimulationClient(backplaneClient);

        await client.StopChannelsAsync(
            0x01,
            0x85,
            new BackplaneConnectionOptions(0x01, TimeSpan.FromSeconds(1)),
            TestContext.Current.CancellationToken);

        var register = Assert.Single(DecodeRegisters(Assert.Single(transport.Requests)));
        Assert.Equal(0x0003, register.Address);
        Assert.Equal(0x00000085U, register.Value);
    }

    [Fact]
    public async Task EmergencyStopBackplaneAsync_WritesZeroToBackplaneStopRegister()
    {
        var transport = new RecordingStatusTransport();
        await using var backplaneClient = new BackplaneClient(new EmptyDiscovery(), transport);
        var client = new DirectCurrentStimulationClient(backplaneClient);

        await client.EmergencyStopBackplaneAsync(
            new BackplaneConnectionOptions(0x01, TimeSpan.FromSeconds(1)),
            TestContext.Current.CancellationToken);

        Assert.Equal(0, transport.ExchangeCount);
        Assert.Equal(1, transport.SendCount);
        var request = Assert.Single(transport.Requests);
        Assert.True(TesV14ProtocolCodec.TryParseFrame(request, out var frame, out var error), error);
        Assert.NotNull(frame);
        Assert.Equal(TesV14ProtocolConstants.BackplaneAddress, frame.DestinationAddress);
        var register = Assert.Single(DecodeRegisters(request));
        Assert.Equal(0x0003, register.Address);
        Assert.Equal(0U, register.Value);
    }

    [Theory]
    [InlineData(0U)]
    [InlineData(0x100U)]
    public async Task StartChannelsAsync_InvalidMask_RejectsBeforeSending(uint channelMask)
    {
        var transport = new RecordingStatusTransport();
        await using var backplaneClient = new BackplaneClient(new EmptyDiscovery(), transport);
        var client = new DirectCurrentStimulationClient(backplaneClient);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => client.StartChannelsAsync(
                0x01,
                channelMask,
                new BackplaneConnectionOptions(0x01, TimeSpan.FromSeconds(1)),
                TestContext.Current.CancellationToken));

        Assert.Empty(transport.Requests);
    }

    private static DirectCurrentStimulationParameters CreateParameters(
        DirectCurrentDeliveryMode deliveryMode,
        DirectCurrentPolarity polarity) =>
        new(
            BoardAddress: 0x01,
            Channel: 1,
            CurrentMilliampere: 2.00m,
            RampUpSeconds: 0.5m,
            RampDownSeconds: 0.5m,
            TotalDurationSeconds: 120m,
            DeliveryMode: deliveryMode,
            IntervalSeconds: deliveryMode == DirectCurrentDeliveryMode.Intermittent ? 5m : 0m,
            SingleDurationSeconds: deliveryMode == DirectCurrentDeliveryMode.Intermittent ? 60m : 0m,
            Polarity: polarity);

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

    private sealed class RecordingStatusTransport : IBackplaneTransport, IBackplaneOneWayTransport
    {
        public bool IsOpen => true;
        public List<byte[]> Requests { get; } = [];
        public int ExchangeCount { get; private set; }
        public int SendCount { get; private set; }

        public Task OpenAsync(
            UsbBackplaneDevice device,
            TimeSpan timeout,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<byte[]> ExchangeAsync(
            ReadOnlyMemory<byte> request,
            CancellationToken cancellationToken = default)
        {
            ExchangeCount++;
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

        public Task SendAsync(
            ReadOnlyMemory<byte> request,
            CancellationToken cancellationToken = default)
        {
            SendCount++;
            Requests.Add(request.ToArray());
            return Task.CompletedTask;
        }

        public Task CloseAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public ValueTask DisposeAsync() =>
            ValueTask.CompletedTask;
    }
}
