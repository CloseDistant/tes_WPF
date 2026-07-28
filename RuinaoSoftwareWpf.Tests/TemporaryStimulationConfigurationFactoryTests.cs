using RuinaoTesProtocol.V15;
using Xunit;

namespace RuinaoSoftwareWpf.Tests;

public sealed class TemporaryStimulationConfigurationFactoryTests
{
    [Fact]
    public void CreateDirectCurrent_IntervalSecondsBecomeLowPlateauAndChannelMapsToBoard()
    {
        var channel = new ChannelConfig
        {
            Name = "CH 2",
            CurrentMA = "2",
            RampUpS = "1",
            RampDownS = "2",
            DurationS = "120",
            SingleDurationS = "10",
            IntervalS = "5",
            StimulationMode = "间隔",
            Polarity = "不掉转",
        };

        var result = TemporaryStimulationConfigurationFactory.CreateDirectCurrent(channel);

        Assert.Equal(0x01, result.TargetAddress);
        Assert.Equal(2, result.Configuration.ChannelNumber);
        Assert.Equal(120_000U, result.Configuration.TotalTimeMs);
        var waveform = Assert.Single(result.Configuration.Waveforms);
        Assert.Equal(TesV15StimulationMode.DirectCurrentTrapezoid, waveform.Mode);
        Assert.Equal(15_000_000U, waveform.DurationUs);
        Assert.Equal(10_000U, waveform.LowLevelOrPositiveValue);
        Assert.Equal(50_000U, waveform.HighLevelOrNegativeValue);
        Assert.Equal(
            1000U,
            waveform.RisePermilleOrPositiveDurationUs
                + waveform.HoldPermilleOrInterphaseIntervalUs
                + waveform.FallPermilleOrNegativeDurationUs
                + waveform.CustomIdOrSeedOrPeriodIntervalUs);
        Assert.True(waveform.CustomIdOrSeedOrPeriodIntervalUs > 0);
    }

    [Fact]
    public void CreatePulseCurrent_MillisecondsBecomeMicrosecondsAndFallIsZero()
    {
        var parameters = new PulseCurrentParameters(
            CurrentMilliamp: 2,
            PulseWidthMilliseconds: 10,
            RiseWidthMilliseconds: 5,
            IntervalWidthMilliseconds: 20,
            TreatmentDurationSeconds: 120,
            Polarity: PulseCurrentPolarities.NotReversed,
            PlannedTotalCount: 3429);

        var result = TemporaryStimulationConfigurationFactory.CreatePulseCurrent(1, parameters);

        Assert.Equal(0x01, result.TargetAddress);
        Assert.Equal(120_000U, result.Configuration.TotalTimeMs);
        var waveform = Assert.Single(result.Configuration.Waveforms);
        Assert.Equal(35_000U, waveform.DurationUs);
        Assert.Equal(0U, waveform.FallPermilleOrNegativeDurationUs);
        Assert.Equal(
            1000U,
            waveform.RisePermilleOrPositiveDurationUs
                + waveform.HoldPermilleOrInterphaseIntervalUs
                + waveform.FallPermilleOrNegativeDurationUs
                + waveform.CustomIdOrSeedOrPeriodIntervalUs);
    }

    [Fact]
    public void CreatePulseCurrent_ReversedPolaritySwapsFixedRawLevels()
    {
        var parameters = new PulseCurrentParameters(
            2,
            10,
            5,
            20,
            120,
            PulseCurrentPolarities.Reversed,
            3429);

        var result = TemporaryStimulationConfigurationFactory.CreatePulseCurrent(1, parameters);
        var waveform = Assert.Single(result.Configuration.Waveforms);

        Assert.Equal(50_000U, waveform.LowLevelOrPositiveValue);
        Assert.Equal(10_000U, waveform.HighLevelOrNegativeValue);
    }
}
