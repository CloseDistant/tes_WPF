namespace RuinaoSoftwareWpf.Tests;

using RuinaoSoftwareWpf.Views.Renderers;
using Xunit;

public sealed class PulseCurrentWaveformTests
{
    [Theory]
    [InlineData(0, "0.0")]
    [InlineData(0.5, "0.5")]
    [InlineData(1.25, "1.25")]
    [InlineData(15, "15.0")]
    public void CurrentAxis_UsesOneOrTwoDecimalPlaces(double value, string expected)
    {
        Assert.Equal(expected, PulseCurrentWaveformSurface.FormatAxisValue(value));
    }

    [Theory]
    [InlineData(0, "0.0")]
    [InlineData(29, "29.0")]
    [InlineData(31.25, "31.3")]
    [InlineData(3600, "3600.0")]
    public void TimeAxis_UsesOneDecimalPlace(double seconds, string expected)
    {
        Assert.Equal(expected, PulseCurrentWaveformSurface.FormatSeconds(seconds));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0.25, -1)]
    [InlineData(0.5, -2)]
    [InlineData(1.49, -2)]
    [InlineData(1.5, 0)]
    [InlineData(2.99, 0)]
    [InlineData(3.0, -2)]
    [InlineData(3.25, -2)]
    public void ReversedPolarity_OnlyFirstPulseUsesRamp(double seconds, double expected)
    {
        var parameters = CreateParameters(PulseCurrentPolarities.Reversed);

        var current = PulseCurrentWaveformMath.GetSimulatedCurrent(parameters, seconds);

        Assert.Equal(expected, current, 6);
    }

    [Fact]
    public void ZeroRiseWidth_StartsAtTargetWithoutDrawingVerticalConnector()
    {
        var parameters = CreateParameters(
            PulseCurrentPolarities.NotReversed,
            riseMilliseconds: 0,
            pulseMilliseconds: 1000,
            intervalMilliseconds: 1000,
            treatmentSeconds: 5,
            plannedCount: 3);

        var points = PulseCurrentWaveformSurface.CreateWaveformPoints(
            parameters,
            elapsed: 0.25,
            plotWidth: 800);

        Assert.Equal(2, PulseCurrentWaveformMath.GetSimulatedCurrent(parameters, 0), 6);
        Assert.Contains(points, point => point.Seconds == 0 && point.CurrentMilliamp == 2);
        Assert.DoesNotContain(points, point => point.Seconds == 0 && point.CurrentMilliamp == 0);
    }

    [Fact]
    public void WaveformPoints_UseBreakMarkersInsteadOfVerticalConnectors()
    {
        var parameters = CreateParameters(PulseCurrentPolarities.NotReversed);

        var points = PulseCurrentWaveformSurface.CreateWaveformPoints(
            parameters,
            elapsed: 5,
            plotWidth: 800);

        Assert.DoesNotContain(
            points.Zip(points.Skip(1)),
            pair => pair.First.Seconds == pair.Second.Seconds
                && double.IsFinite(pair.First.CurrentMilliamp)
                && double.IsFinite(pair.Second.CurrentMilliamp)
                && pair.First.CurrentMilliamp != pair.Second.CurrentMilliamp);
        Assert.Contains(points, point => double.IsNaN(point.CurrentMilliamp));
    }

    [Fact]
    public void WaveformPoints_DoNotDrawZeroCurrentIntervals()
    {
        var parameters = CreateParameters(PulseCurrentPolarities.NotReversed);

        var points = PulseCurrentWaveformSurface.CreateWaveformPoints(
            parameters,
            elapsed: 10,
            plotWidth: 800);

        Assert.DoesNotContain(
            points.Zip(points.Skip(1)),
            pair => double.IsFinite(pair.First.CurrentMilliamp)
                && double.IsFinite(pair.Second.CurrentMilliamp)
                && Math.Abs(pair.First.CurrentMilliamp) < 0.000001
                && Math.Abs(pair.Second.CurrentMilliamp) < 0.000001);
    }

    [Fact]
    public void BoundedWaveformPoints_DoNotDrawZeroCurrentIntervals()
    {
        var parameters = CreateParameters(
            PulseCurrentPolarities.NotReversed,
            riseMilliseconds: 5,
            pulseMilliseconds: 10,
            intervalMilliseconds: 10,
            treatmentSeconds: 1200,
            plannedCount: 60000);

        var points = PulseCurrentWaveformSurface.CreateWaveformPoints(
            parameters,
            visibleStart: 0,
            visibleEnd: 1200,
            plotWidth: 800);

        Assert.DoesNotContain(
            points,
            point => double.IsFinite(point.CurrentMilliamp)
                && Math.Abs(point.CurrentMilliamp) < 0.000001);
    }

    [Fact]
    public void FinalIncompleteCycle_RemainsAtZeroUntilTreatmentEnds()
    {
        var parameters = CreateParameters(
            PulseCurrentPolarities.NotReversed,
            treatmentSeconds: 12,
            plannedCount: 4);

        Assert.Equal(0, PulseCurrentWaveformMath.GetSimulatedCurrent(parameters, 10.5), 6);
        Assert.Equal(0, PulseCurrentWaveformMath.GetSimulatedCurrent(parameters, 11.5), 6);
        Assert.Equal(0, PulseCurrentWaveformMath.GetSimulatedCurrent(parameters, 12), 6);
    }

    [Theory]
    [InlineData(0.49, 0)]
    [InlineData(1.49, 0)]
    [InlineData(1.5, 1)]
    [InlineData(3.99, 1)]
    [InlineData(4.0, 2)]
    [InlineData(7.0, 3)]
    [InlineData(10.0, 4)]
    public void CompletedCount_IncrementsWhenFullPulseEnds(double seconds, long expected)
    {
        var parameters = CreateParameters(PulseCurrentPolarities.NotReversed);

        var count = PulseCurrentWaveformMath.GetCompletedPulseCount(parameters, seconds);

        Assert.Equal(expected, count);
    }

    [Fact]
    public void EmergencyStop_RetainsCompletedCountAndElapsedWaveform()
    {
        var parameters = CreateParameters(PulseCurrentPolarities.NotReversed);
        var state = new PulseCurrentWaveformState { IsGlobalView = true };
        state.Start(parameters);
        state.UpdateElapsed(4.8);

        state.EmergencyStop(4.8);

        Assert.Equal(PulseCurrentWaveformRunState.EmergencyStopped, state.RunState);
        Assert.Equal(2, state.CompletedPulseCount);
        Assert.Equal(4.8, state.ElapsedSeconds, 6);
        var window = PulseCurrentWaveformSurface.GetTimeWindow(
            state,
            parameters,
            state.ElapsedSeconds);
        Assert.Equal(0, window.Start);
        Assert.Equal(4.8, window.End, 6);
    }

    [Fact]
    public void GlobalTimeAxis_UsesActualElapsedTimeInsteadOfPlannedTreatmentTime()
    {
        var parameters = CreateParameters(
            PulseCurrentPolarities.NotReversed,
            treatmentSeconds: 1200);
        var state = new PulseCurrentWaveformState { IsGlobalView = true };

        var window = PulseCurrentWaveformSurface.GetTimeWindow(state, parameters, 18.4);

        Assert.Equal(0, window.Start);
        Assert.Equal(18.4, window.End, 6);
    }

    [Fact]
    public void DetailView_UsesRecentEightPulseCyclesOnActualTimeAxis()
    {
        var parameters = CreateParameters(
            PulseCurrentPolarities.NotReversed,
            pulseMilliseconds: 10,
            intervalMilliseconds: 120,
            treatmentSeconds: 1200);
        var state = new PulseCurrentWaveformState();

        var window = PulseCurrentWaveformSurface.GetTimeWindow(state, parameters, 32.4);

        Assert.Equal(31.36, window.Start, 6);
        Assert.Equal(32.4, window.End, 6);
    }

    [Fact]
    public void DetailView_DoesNotShowFutureTimeBeforeOneWindowHasElapsed()
    {
        var parameters = CreateParameters(
            PulseCurrentPolarities.NotReversed,
            pulseMilliseconds: 10,
            intervalMilliseconds: 120,
            treatmentSeconds: 1200);
        var state = new PulseCurrentWaveformState();

        var window = PulseCurrentWaveformSurface.GetTimeWindow(state, parameters, 0.4);

        Assert.Equal(0, window.Start);
        Assert.Equal(0.4, window.End, 6);
    }

    [Fact]
    public void ReversedPolarity_VerticalScaleExtendsBelowTarget()
    {
        var parameters = CreateParameters(PulseCurrentPolarities.Reversed);

        var scale = PulseCurrentWaveformSurface.CreateYScale(parameters);

        Assert.Equal(0, scale.Maximum);
        Assert.True(scale.Minimum < -parameters.CurrentMilliamp);
    }

    private static PulseCurrentParameters CreateParameters(
        string polarity,
        int riseMilliseconds = 500,
        int pulseMilliseconds = 1000,
        int intervalMilliseconds = 1500,
        double treatmentSeconds = 12,
        long plannedCount = 4)
    {
        return new PulseCurrentParameters(
            2,
            pulseMilliseconds,
            riseMilliseconds,
            intervalMilliseconds,
            treatmentSeconds,
            polarity,
            plannedCount);
    }
}
