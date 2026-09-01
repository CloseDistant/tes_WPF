using Xunit;

namespace RuinaoSoftwareWpf.Tests;

public sealed class PulseCurrentParametersTests
{
    [Fact]
    public void ChannelConfig_DefaultPolarityIsNotReversed()
    {
        var channel = new PulseCurrentChannelConfig();

        Assert.Equal(PulseCurrentPolarities.NotReversed, channel.Polarity);
    }

    [Fact]
    public void PolarityOptions_UseReversalTerminology()
    {
        Assert.Equal(
            ["不掉转", "调转"],
            PulseCurrentPolarities.All);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("未知")]
    public void ChannelConfig_InvalidTransientPolarity_DoesNotClearExistingSelection(string? transientValue)
    {
        var channel = new PulseCurrentChannelConfig
        {
            Polarity = PulseCurrentPolarities.Reversed
        };

        channel.Polarity = transientValue!;

        Assert.Equal(PulseCurrentPolarities.Reversed, channel.Polarity);
    }

    [Fact]
    public void TryCreate_LastPulseDoesNotRequireTrailingInterval()
    {
        var channel = CreateValidChannel();
        channel.PulseWidthMilliseconds = "100";
        channel.RiseWidthMilliseconds = "100";
        channel.IntervalWidthMilliseconds = "100";
        channel.TreatmentDurationSeconds = "1";

        var success = PulseCurrentParameters.TryCreate(channel, out var parameters, out var error);

        Assert.True(success, error);
        Assert.NotNull(parameters);
        Assert.Equal(5, parameters.PlannedTotalCount);
        Assert.Equal(1.1, parameters.TotalRuntimeSeconds, 6);
    }

    [Fact]
    public void TryCreate_RiseWidthDoesNotReducePulsesInsideTreatmentTime()
    {
        var channel = CreateValidChannel();
        channel.PulseWidthMilliseconds = "100";
        channel.IntervalWidthMilliseconds = "100";
        channel.TreatmentDurationSeconds = "1";
        channel.RiseWidthMilliseconds = "0";
        Assert.True(PulseCurrentParameters.TryCreate(channel, out var withoutRise, out var firstError), firstError);

        channel.RiseWidthMilliseconds = "900";
        Assert.True(PulseCurrentParameters.TryCreate(channel, out var withRise, out var secondError), secondError);

        Assert.Equal(withoutRise!.PlannedTotalCount, withRise!.PlannedTotalCount);
        Assert.Equal(1.9, withRise.TotalRuntimeSeconds, 6);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-0.1")]
    [InlineData("15.1")]
    public void TryCreate_RejectsInvalidCurrent(string current)
    {
        var channel = CreateValidChannel();
        channel.CurrentMilliamp = current;

        var success = PulseCurrentParameters.TryCreate(channel, out _, out var error);

        Assert.False(success);
        Assert.Contains("幅值", error);
    }

    [Fact]
    public void TryCreate_RejectsZeroPulseWidth()
    {
        var channel = CreateValidChannel();
        channel.PulseWidthMilliseconds = "0";

        var success = PulseCurrentParameters.TryCreate(channel, out _, out var error);

        Assert.False(success);
        Assert.Contains("脉冲宽度请输入 1～2000", error);
    }

    [Fact]
    public void TryCreate_AllowsZeroRiseWidth()
    {
        var channel = CreateValidChannel();
        channel.RiseWidthMilliseconds = "0";

        var success = PulseCurrentParameters.TryCreate(channel, out var parameters, out var error);

        Assert.True(success, error);
        Assert.NotNull(parameters);
        Assert.Equal(0, parameters.RiseWidthMilliseconds);
    }

    [Fact]
    public void TryCreate_AllowsOneDecimalTreatmentSeconds()
    {
        var channel = CreateValidChannel();
        channel.TreatmentDurationSeconds = "1.5";

        var success = PulseCurrentParameters.TryCreate(channel, out var parameters, out var error);

        Assert.True(success, error);
        Assert.NotNull(parameters);
        Assert.Equal(1.5, parameters.TreatmentDurationSeconds);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("3600.1")]
    public void TryCreate_RequiresTreatmentSecondsInConfirmedRange(string duration)
    {
        var channel = CreateValidChannel();
        channel.TreatmentDurationSeconds = duration;

        var success = PulseCurrentParameters.TryCreate(channel, out _, out var error);

        Assert.False(success);
        Assert.Contains("治疗时间", error);
    }

    [Fact]
    public void TryCreate_RejectsZeroIntervalWidth()
    {
        var channel = CreateValidChannel();
        channel.IntervalWidthMilliseconds = "0";

        var success = PulseCurrentParameters.TryCreate(channel, out _, out var error);

        Assert.False(success);
        Assert.Contains("间隔宽度", error);
    }

    [Fact]
    public void TryCreate_AllowsTwoSecondPulseWidth()
    {
        var channel = CreateValidChannel();
        channel.PulseWidthMilliseconds = "2000";

        var success = PulseCurrentParameters.TryCreate(channel, out _, out var error);

        Assert.True(success, error);
    }

    [Fact]
    public void EditingParameter_ClearsPreviouslyDisplayedPlannedCount()
    {
        var channel = CreateValidChannel();
        channel.ShowPlannedTotalCount(42);

        channel.IntervalWidthMilliseconds = "25";

        Assert.Equal("—", channel.PlannedTotalCount);
    }

    private static PulseCurrentChannelConfig CreateValidChannel()
    {
        return new PulseCurrentChannelConfig
        {
            Name = "CH 1",
            CurrentMilliamp = "2",
            PulseWidthMilliseconds = "10",
            RiseWidthMilliseconds = "5",
            IntervalWidthMilliseconds = "20",
            TreatmentDurationSeconds = "1200",
            Polarity = PulseCurrentPolarities.NotReversed
        };
    }
}
