using Xunit;

namespace RuinaoSoftwareWpf.Tests;

public sealed class MonophasicPulseCurrentParameterRulesTests
{
    [Fact]
    public void TryCreateWaveform_UsesOneRampValueForBothTriangleSides()
    {
        var channel = CreateChannel();
        channel.CurrentMA = "5.00";
        channel.RampUpS = "1.0";
        channel.IntervalS = "0.0";
        channel.DurationS = "10.0";

        var valid = MonophasicPulseCurrentParameterRules.TryCreateWaveform(
            channel,
            out var parameters,
            out var error);

        Assert.True(valid, error);
        Assert.Equal(1d, parameters!.RampUpSeconds);
        Assert.Equal(1d, parameters.RampDownSeconds);
        Assert.Equal(0d, parameters.PlateauSeconds);
        Assert.False(parameters.IsContinuous);
        Assert.False(parameters.ReversePolarity);
    }

    [Theory]
    [InlineData(MonophasicPulseCurrentParameterKind.CurrentMilliamp, "0.00")]
    [InlineData(MonophasicPulseCurrentParameterKind.CurrentMilliamp, "15.01")]
    [InlineData(MonophasicPulseCurrentParameterKind.RampSeconds, "0.0")]
    [InlineData(MonophasicPulseCurrentParameterKind.RampSeconds, "100.1")]
    [InlineData(MonophasicPulseCurrentParameterKind.IntervalSeconds, "3600.1")]
    [InlineData(MonophasicPulseCurrentParameterKind.TotalDurationSeconds, "3600.1")]
    public void TryParseValidated_RejectsOutOfRangeValues(
        MonophasicPulseCurrentParameterKind kind,
        string text)
    {
        Assert.False(MonophasicPulseCurrentParameterRules.TryParseValidated(
            kind,
            text,
            out _,
            out _));
    }

    [Fact]
    public void TryCreateWaveform_RejectsTotalShorterThanSymmetricTriangle()
    {
        var channel = CreateChannel();
        channel.RampUpS = "1.0";
        channel.DurationS = "1.9";

        Assert.False(MonophasicPulseCurrentParameterRules.TryCreateWaveform(
            channel,
            out _,
            out var error));
        Assert.Contains("2×渐升时间", error);
    }

    private static ChannelConfig CreateChannel() => new()
    {
        Name = "CH 1",
        CurrentMA = "0.01",
        RampUpS = "0.5",
        RampDownS = "0.5",
        IntervalS = "0.0",
        DurationS = "120.0",
        SingleDurationS = "1.0",
        StimulationMode = "间隔",
        Polarity = "不掉转"
    };
}
