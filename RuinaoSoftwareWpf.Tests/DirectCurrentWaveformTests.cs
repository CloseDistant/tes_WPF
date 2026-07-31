namespace RuinaoSoftwareWpf.Tests;

using RuinaoSoftwareWpf.Views.Renderers;
using Xunit;

public sealed class DirectCurrentWaveformTests
{
    [Theory]
    [InlineData(0, "0.0")]
    [InlineData(0.5, "0.5")]
    [InlineData(1.25, "1.25")]
    [InlineData(2, "2.0")]
    public void CurrentAxis_UsesOneOrTwoDecimalPlaces(double value, string expected)
    {
        Assert.Equal(expected, DirectCurrentWaveformSurface.FormatAxisValue(value));
    }

    [Theory]
    [InlineData(0, "0.0")]
    [InlineData(31, "31.0")]
    [InlineData(31.25, "31.3")]
    [InlineData(3600, "3600.0")]
    public void TimeAxis_UsesOneDecimalPlace(double seconds, string expected)
    {
        Assert.Equal(expected, DirectCurrentWaveformSurface.FormatSeconds(seconds));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(15, 1)]
    [InlineData(30, 2)]
    [InlineData(90, 2)]
    [InlineData(105, 1)]
    [InlineData(120, 0)]
    public void ContinuousWaveform_UsesRampPlateauAndFinalRampDown(double seconds, double expected)
    {
        var parameters = new DirectCurrentWaveformParameters(2, 30, 30, 120, 0, 0, true, false);

        var actual = DirectCurrentWaveformSurface.GetSimulatedCurrent(parameters, seconds);

        Assert.Equal(expected, actual, 6);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(15, -1)]
    [InlineData(30, -2)]
    [InlineData(90, -2)]
    [InlineData(105, -1)]
    [InlineData(120, 0)]
    public void ReversedContinuousWaveform_DrawsBelowZero(
        double seconds,
        double expected)
    {
        var parameters = new DirectCurrentWaveformParameters(
            2,
            30,
            30,
            120,
            0,
            0,
            true,
            true);

        var actual = DirectCurrentWaveformSurface.GetSimulatedCurrent(parameters, seconds);

        Assert.Equal(expected, actual, 6);
    }

    [Fact]
    public void IntervalWaveform_DoesNotStartPulseWhenRemainingTimeCannotFitBothRamps()
    {
        var parameters = new DirectCurrentWaveformParameters(2, 10, 10, 78, 5, 20, false, false);

        // 标准第一轮长 45 秒；第二轮只剩 33 秒，平台缩短到 13 秒并在 78 秒归零。
        Assert.Equal(2, DirectCurrentWaveformSurface.GetSimulatedCurrent(parameters, 60), 6);
        Assert.Equal(1, DirectCurrentWaveformSurface.GetSimulatedCurrent(parameters, 73), 6);
        Assert.Equal(0, DirectCurrentWaveformSurface.GetSimulatedCurrent(parameters, 78), 6);

        var tooShort = parameters with { TotalDurationSeconds = 58 };
        // 第一轮结束后只剩 13 秒，不足以容纳 10 秒渐升和 10 秒渐降，不产生第二个残缺脉冲。
        Assert.Equal(0, DirectCurrentWaveformSurface.GetSimulatedCurrent(tooShort, 50), 6);
    }

    [Fact]
    public void IntervalWaveform_SingleDurationIncludesRampUpAndRampDown()
    {
        var channel = new ChannelConfig
        {
            Name = "CH 1",
            CurrentMA = "2",
            RampUpS = "10",
            RampDownS = "10",
            DurationS = "120",
            StimulationMode = "间隔",
            IntervalS = "5",
            SingleDurationS = "30"
        };

        Assert.True(DirectCurrentWaveformParameters.TryCreate(channel, out var parameters, out var error), error);
        Assert.NotNull(parameters);
        Assert.Equal(10, parameters.PlateauSeconds, 6);

        // 完整刺激段为30秒：渐升10秒 + 平台10秒 + 渐降10秒；随后间隔5秒。
        Assert.Equal(1, DirectCurrentWaveformSurface.GetSimulatedCurrent(parameters, 5), 6);
        Assert.Equal(2, DirectCurrentWaveformSurface.GetSimulatedCurrent(parameters, 15), 6);
        Assert.Equal(1, DirectCurrentWaveformSurface.GetSimulatedCurrent(parameters, 25), 6);
        Assert.Equal(0, DirectCurrentWaveformSurface.GetSimulatedCurrent(parameters, 30), 6);
        Assert.Equal(0, DirectCurrentWaveformSurface.GetSimulatedCurrent(parameters, 34), 6);
        Assert.Equal(1, DirectCurrentWaveformSurface.GetSimulatedCurrent(parameters, 40), 6);
    }

    [Fact]
    public void IntervalWaveform_RejectsSingleDurationShorterThanBothRamps()
    {
        var channel = new ChannelConfig
        {
            Name = "CH 1",
            CurrentMA = "2",
            RampUpS = "10",
            RampDownS = "10",
            DurationS = "120",
            StimulationMode = "间隔",
            IntervalS = "5",
            SingleDurationS = "19"
        };

        Assert.False(DirectCurrentWaveformParameters.TryCreate(channel, out _, out var error));
        Assert.Contains("必须大于渐升时间与渐降时间之和", error);
    }

    [Fact]
    public void ReversedIntervalWaveform_DrawsEveryCycleBelowZero()
    {
        var parameters = new DirectCurrentWaveformParameters(2, 10, 10, 120, 5, 20, false, true);

        Assert.Equal(-2, DirectCurrentWaveformSurface.GetSimulatedCurrent(parameters, 20), 6);
        Assert.Equal(-2, DirectCurrentWaveformSurface.GetSimulatedCurrent(parameters, 65), 6);
    }

    [Fact]
    public void ParameterValidation_IgnoresDisabledIntervalFieldsInContinuousMode()
    {
        var channel = new ChannelConfig
        {
            Name = "CH 1",
            CurrentMA = "2",
            RampUpS = "30",
            RampDownS = "30",
            DurationS = "1200",
            StimulationMode = "连续",
            IntervalS = "not-used",
            SingleDurationS = "not-used"
        };

        var valid = DirectCurrentWaveformParameters.TryCreate(channel, out var parameters, out var error);

        Assert.True(valid, error);
        Assert.NotNull(parameters);
        Assert.True(parameters.IsContinuous);
    }

    [Fact]
    public void GlobalView_UsesActualElapsedTimeInsteadOfTargetDuration()
    {
        var parameters = new DirectCurrentWaveformParameters(2, 30, 30, 1200, 0, 0, true, false);
        var state = new DirectCurrentWaveformState { IsGlobalView = true };
        state.Start(parameters);
        state.UpdateElapsed(18.4);

        var window = DirectCurrentWaveformSurface.GetTimeWindow(state, parameters, state.ElapsedSeconds);

        Assert.Equal(0, window.Start);
        Assert.Equal(18.4, window.End, 6);
    }

    [Theory]
    [InlineData(2, 0.5, 2.5)]
    [InlineData(1, 0.25, 1.25)]
    [InlineData(0.2, 0.05, 0.25)]
    public void VerticalAxis_LeavesOneReadableTickAboveTarget(
        double currentMilliamp,
        double expectedTick,
        double expectedMaximum)
    {
        var parameters = new DirectCurrentWaveformParameters(
            currentMilliamp, 30, 30, 1200, 0, 0, true, false);

        var scale = DirectCurrentWaveformSurface.CreateYScale(parameters);

        Assert.Equal(expectedTick, scale.TickStep, 6);
        Assert.Equal(expectedMaximum, scale.Maximum, 6);
        Assert.True(scale.Maximum > currentMilliamp);
    }

    [Fact]
    public void ReversedContinuousWaveform_VerticalAxisExtendsBelowTarget()
    {
        var parameters = new DirectCurrentWaveformParameters(
            2,
            30,
            30,
            1200,
            0,
            0,
            true,
            true);

        var scale = DirectCurrentWaveformSurface.CreateYScale(parameters);

        Assert.Equal(0, scale.Maximum);
        Assert.True(scale.Minimum < -parameters.CurrentMilliamp);
    }

    [Fact]
    public void ReversedIntervalWaveform_VerticalAxisOnlyDisplaysNegativeRange()
    {
        var parameters = new DirectCurrentWaveformParameters(
            2,
            10,
            10,
            120,
            5,
            20,
            false,
            true);

        var scale = DirectCurrentWaveformSurface.CreateYScale(parameters);

        Assert.Equal(0, scale.Maximum);
        Assert.True(scale.Minimum < -parameters.CurrentMilliamp);
    }
}
