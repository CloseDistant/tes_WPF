using RuinaoSoftwareWpf.Views.Renderers;
using Xunit;

namespace RuinaoSoftwareWpf.Tests;

public sealed class TiAlternatingCurrentWaveformTests
{
    [Fact]
    public void PreviewFactory_ReusesSharedDllFiveSegmentPlan()
    {
        var preview = new TiWaveformPreviewFactory().Create(
            new TiAlternatingCurrentParameters(
                1.000m,
                2.0m,
                2.0m,
                1000,
                10.0m));

        Assert.Equal(5, preview.Segments.Count);
        Assert.Equal([0.33, 0.67, 1.0, 0.67, 0.33],
            preview.Segments.Select(segment => segment.PeakCurrentMilliampere),
            new RoundedDoubleComparer(2));
        Assert.Equal(10, preview.Segments.Sum(segment => segment.DurationSeconds), 6);
    }

    [Fact]
    public void WaveformState_FreezesPreviewAndTracksElapsedTime()
    {
        var preview = new TiWaveformPreviewFactory().Create(
            new TiAlternatingCurrentParameters(0.010m, 0.5m, 0.5m, 1000, 1200m));
        var state = new AlternatingCurrentWaveformState();

        state.Start(preview);
        state.UpdateElapsed(4.25);

        Assert.True(state.HasWaveform);
        Assert.True(state.IsRunning);
        Assert.Equal(4.25, state.ElapsedSeconds);
        state.Stop(4.5);
        Assert.False(state.IsRunning);
        Assert.Equal(4.5, state.ElapsedSeconds);
    }

    [Fact]
    public void SimulatedCurrent_UsesActiveSegmentPeakAndCarrierFrequency()
    {
        var preview = new TiWaveformPreviewFactory().Create(
            new TiAlternatingCurrentParameters(1.000m, 2.0m, 2.0m, 1000, 10m));

        var current = AlternatingCurrentWaveformSurface.GetSimulatedCurrent(
            preview,
            0.00025);

        Assert.Equal(0.33, current, 6);
    }

    private sealed class RoundedDoubleComparer(int decimalPlaces) : IEqualityComparer<double>
    {
        public bool Equals(double left, double right) =>
            Math.Round(left, decimalPlaces) == Math.Round(right, decimalPlaces);

        public int GetHashCode(double value) => Math.Round(value, decimalPlaces).GetHashCode();
    }
}
