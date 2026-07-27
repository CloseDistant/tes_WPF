namespace RuinaoSoftwareWpf.Tests;

using RuinaoSoftwareWpf.Views.Renderers;
using Xunit;

public sealed class PulseCurrentWaveformTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(0.25, -1)]
    [InlineData(0.5, -2)]
    [InlineData(1.49, -2)]
    [InlineData(1.5, 0)]
    [InlineData(2.99, 0)]
    [InlineData(3.25, -1)]
    public void ReversedPolarity_UsesRampPlateauAndZeroInterval(double seconds, double expected)
    {
        var parameters = CreateParameters(PulseCurrentPolarities.Reversed);

        var current = PulseCurrentWaveformMath.GetSimulatedCurrent(parameters, seconds);

        Assert.Equal(expected, current, 6);
    }

    [Fact]
    public void ZeroRiseWidth_JumpsVerticallyToTargetCurrent()
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
        Assert.Contains(points, point => point.Seconds == 0 && point.CurrentMilliamp == 0);
        Assert.Contains(points, point => point.Seconds == 0 && point.CurrentMilliamp == 2);
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
    [InlineData(4.5, 2)]
    [InlineData(12, 4)]
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

    [Theory]
    [InlineData(18.4, 0, 60)]
    [InlineData(78.4, 60, 120)]
    [InlineData(121.0, 120, 180)]
    public void SixtySecondView_UsesCurrentTimePage(
        double elapsed,
        double expectedStart,
        double expectedEnd)
    {
        var parameters = CreateParameters(
            PulseCurrentPolarities.NotReversed,
            treatmentSeconds: 1200);
        var state = new PulseCurrentWaveformState();

        var window = PulseCurrentWaveformSurface.GetTimeWindow(state, parameters, elapsed);

        Assert.Equal(expectedStart, window.Start);
        Assert.Equal(expectedEnd, window.End);
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
        double riseMilliseconds = 500,
        double pulseMilliseconds = 1000,
        double intervalMilliseconds = 1500,
        int treatmentSeconds = 12,
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
