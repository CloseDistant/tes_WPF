namespace RuinaoSoftwareWpf.Tests;

using Xunit;

public sealed class DirectCurrentParameterRulesTests
{
    [Theory]
    [InlineData("1.235", "1.24")]
    [InlineData("0.014", "0.01")]
    [InlineData("15", "15.00")]
    public void NormalizeCurrent_RoundsToTwoDecimalsWithoutWarning(string input, string expected)
    {
        var result = DirectCurrentParameterRules.Normalize(
            DirectCurrentParameterKind.CurrentMilliamp,
            input,
            DirectCurrentParameterRules.DefaultCurrentMilliamp);

        Assert.True(result.IsValid);
        Assert.Equal(expected, result.Value);
    }

    [Fact]
    public void NormalizeCurrent_AboveMaximumClampsAndRequestsToast()
    {
        var result = DirectCurrentParameterRules.Normalize(
            DirectCurrentParameterKind.CurrentMilliamp,
            "15.345",
            DirectCurrentParameterRules.DefaultCurrentMilliamp);

        Assert.False(result.IsValid);
        Assert.Equal("15.00", result.Value);
        Assert.Contains("已调整", result.ErrorMessage);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("0")]
    public void NormalizeCurrent_InvalidInputRestoresPreviousValue(string input)
    {
        var result = DirectCurrentParameterRules.Normalize(
            DirectCurrentParameterKind.CurrentMilliamp,
            input,
            "1.25");

        Assert.False(result.IsValid);
        Assert.Equal("1.25", result.Value);
        Assert.Contains("0.01～15.00", result.ErrorMessage);
    }

    [Theory]
    [InlineData("12.35", "12.4")]
    [InlineData("0", "0.0")]
    public void NormalizeNonNegativeTime_RoundsToOneDecimalWithoutWarning(string input, string expected)
    {
        var result = DirectCurrentParameterRules.Normalize(
            DirectCurrentParameterKind.IntervalSeconds,
            input,
            DirectCurrentParameterRules.DefaultIntervalSeconds);

        Assert.True(result.IsValid);
        Assert.Equal(expected, result.Value);
    }

    [Fact]
    public void NormalizeTime_AboveMaximumClampsAndRequestsToast()
    {
        var result = DirectCurrentParameterRules.Normalize(
            DirectCurrentParameterKind.IntervalSeconds,
            "3600.16",
            DirectCurrentParameterRules.DefaultIntervalSeconds);

        Assert.False(result.IsValid);
        Assert.Equal("3600.0", result.Value);
        Assert.Contains("已调整", result.ErrorMessage);
    }

    [Fact]
    public void TryCreate_RejectsCurrentAboveMaximumEvenWhenUiNormalizationIsBypassed()
    {
        var channel = CreateValidChannel();
        channel.CurrentMA = "15.01";

        var created = DirectCurrentWaveformParameters.TryCreate(channel, out _, out var error);

        Assert.False(created);
        Assert.Contains("15.00 mA", error);
    }

    [Fact]
    public void TryCreate_RejectsExcessPrecisionEvenWhenUiNormalizationIsBypassed()
    {
        var channel = CreateValidChannel();
        channel.RampUpS = "0.55";

        var created = DirectCurrentWaveformParameters.TryCreate(channel, out _, out var error);

        Assert.False(created);
        Assert.Contains("0.1 s", error);
    }

    [Fact]
    public void TryCreate_RequiresSingleDurationStrictlyGreaterThanBothRamps()
    {
        var channel = CreateValidChannel();
        channel.RampUpS = "0.5";
        channel.RampDownS = "0.5";
        channel.SingleDurationS = "1.0";

        var created = DirectCurrentWaveformParameters.TryCreate(channel, out _, out var error);

        Assert.False(created);
        Assert.Contains("必须大于", error);
    }

    private static ChannelConfig CreateValidChannel() => new()
    {
        Name = "CH 1",
        CurrentMA = "0.01",
        RampUpS = "0.5",
        RampDownS = "0.5",
        DurationS = "1200.0",
        IntervalS = "0.0",
        SingleDurationS = "60.0",
        StimulationMode = "间隔"
    };
}
