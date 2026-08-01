using RuinaoHardwareEngineer.Features.RawStimulation;
using Xunit;

namespace RuinaoHardwareEngineer.Tests;

public sealed class UsbTest4StimulusValueConverterTests
{
    [Theory]
    [InlineData(1U)]
    [InlineData(2U)]
    [InlineData(3U)]
    [InlineData(4U)]
    [InlineData(5U)]
    [InlineData(6U)]
    [InlineData(7U)]
    [InlineData(8U)]
    [InlineData(9U)]
    [InlineData(10U)]
    [InlineData(11U)]
    public void CreateWaveformDefault_ReturnsRequestedUsbTest4Type(uint waveformType)
    {
        var waveform = UsbTest4WaveformDefaults.Create(waveformType);

        Assert.Equal(waveformType, waveform.WaveformType);
        Assert.NotEqual(0U, waveform.DurationUs);
    }

    [Fact]
    public void CreatePulseDefault_UsesUsbTest4V16InitialValues()
    {
        var waveform = UsbTest4WaveformDefaults.Create(10);

        Assert.Equal(12_000U, waveform.LowLevelOrPositiveValue);
        Assert.Equal(unchecked((uint)-12_000), waveform.HighLevelOrNegativeValue);
        Assert.Equal(5_000U, waveform.RisePermilleOrPositiveDurationUs);
        Assert.Equal(2_000U, waveform.HoldPermilleOrInterphaseIntervalUs);
        Assert.Equal(5_000U, waveform.FallPermilleOrNegativeDurationUs);
        Assert.Equal(8_000U, waveform.CustomIdOrSeedOrPeriodIntervalUs);
    }

    [Theory]
    [InlineData(15, 15, 32767)]
    [InlineData(7.5, 15, 16384)]
    [InlineData(1, 20, 1638)]
    public void CurrentAmplitudeToRegister_UsesUsbTest4Scale(
        double currentMilliampere,
        double maxCurrentMilliampere,
        uint expectedDa)
    {
        var registerValue = UsbTest4StimulusValueConverter.CurrentAmplitudeToRegister(
            (decimal)currentMilliampere,
            (decimal)maxCurrentMilliampere);

        Assert.Equal(expectedDa, registerValue);
    }

    [Fact]
    public void SignedCurrent_RoundTrip_PreservesNegativePolarity()
    {
        const decimal maxCurrentMilliampere = 15M;

        var registerValue = UsbTest4StimulusValueConverter.CurrentToRegister(
            -5M,
            maxCurrentMilliampere);
        var restored = UsbTest4StimulusValueConverter.RegisterToCurrent(
            registerValue,
            maxCurrentMilliampere);

        Assert.Equal(-5M, restored);
    }

    [Theory]
    [InlineData(9.999)]
    [InlineData(20.001)]
    public void ValidateMaxCurrent_WhenOutsideUsbTest4Range_Throws(double value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => UsbTest4StimulusValueConverter.ValidateMaxCurrentMilliampere((decimal)value));
    }
}
