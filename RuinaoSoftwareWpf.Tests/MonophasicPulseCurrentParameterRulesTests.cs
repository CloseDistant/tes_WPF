using Xunit;

namespace RuinaoSoftwareWpf.Tests;

public sealed class MonophasicPulseCurrentParameterRulesTests
{
    [Theory]
    [InlineData(MonophasicPulseCurrentParameterKind.CurrentMilliamp, "15.001", "15.00")]
    [InlineData(MonophasicPulseCurrentParameterKind.RampSeconds, "100.1", "100.0")]
    [InlineData(MonophasicPulseCurrentParameterKind.IntervalSeconds, "3600.1", "3600.0")]
    [InlineData(MonophasicPulseCurrentParameterKind.TotalDurationSeconds, "3600.1", "3600.0")]
    public void Normalize_AboveMaximumClampsAndRequestsToast(
        MonophasicPulseCurrentParameterKind kind,
        string input,
        string expected)
    {
        var result = MonophasicPulseCurrentParameterRules.Normalize(kind, input, "1.0");

        Assert.False(result.IsValid);
        Assert.Equal(expected, result.Value);
        Assert.Contains("已调整", result.ErrorMessage);
    }

    [Theory]
    [InlineData(MonophasicPulseCurrentParameterKind.CurrentMilliamp, "1.235", "1.24")]
    [InlineData(MonophasicPulseCurrentParameterKind.RampSeconds, "12.35", "12.4")]
    [InlineData(MonophasicPulseCurrentParameterKind.IntervalSeconds, "3.04", "3.0")]
    [InlineData(MonophasicPulseCurrentParameterKind.TotalDurationSeconds, "120.05", "120.1")]
    public void Normalize_ExcessPrecisionRoundsWithoutToast(
        MonophasicPulseCurrentParameterKind kind,
        string input,
        string expected)
    {
        var result = MonophasicPulseCurrentParameterRules.Normalize(kind, input, "1.0");

        Assert.True(result.IsValid);
        Assert.Equal(expected, result.Value);
        Assert.Empty(result.ErrorMessage);
    }

    [Theory]
    [InlineData(MonophasicPulseCurrentParameterKind.CurrentMilliamp, "abc")]
    [InlineData(MonophasicPulseCurrentParameterKind.CurrentMilliamp, "0")]
    [InlineData(MonophasicPulseCurrentParameterKind.RampSeconds, "0")]
    [InlineData(MonophasicPulseCurrentParameterKind.TotalDurationSeconds, "0.1")]
    public void Normalize_InvalidOrBelowMinimumRestoresPreviousValue(
        MonophasicPulseCurrentParameterKind kind,
        string input)
    {
        const string previousValue = "1.5";

        var result = MonophasicPulseCurrentParameterRules.Normalize(kind, input, previousValue);

        Assert.False(result.IsValid);
        Assert.Equal(previousValue, result.Value);
        Assert.NotEmpty(result.ErrorMessage);
    }

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
