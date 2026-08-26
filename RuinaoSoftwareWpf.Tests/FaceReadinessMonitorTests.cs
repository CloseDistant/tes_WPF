namespace RuinaoSoftwareWpf.Tests;

using Xunit;

public sealed class FaceReadinessMonitorTests
{
    private const long Frequency = 1000;

    [Fact]
    public void Observe_TransitionsFromStabilizingToReadyAfterContinuousConfirmation()
    {
        var monitor = new FaceReadinessMonitor(TimeSpan.FromSeconds(1.5), Frequency);

        var started = monitor.Observe(meetsRequirements: true, timestamp: 1000);
        var almostReady = monitor.Observe(meetsRequirements: true, timestamp: 2499);
        var ready = monitor.Observe(meetsRequirements: true, timestamp: 2500);

        Assert.Equal(FaceReadinessState.Stabilizing, started.State);
        Assert.Equal(0, started.ProgressPercent);
        Assert.Equal(FaceReadinessState.Stabilizing, almostReady.State);
        Assert.False(almostReady.IsReady);
        Assert.Equal(FaceReadinessState.Ready, ready.State);
        Assert.True(ready.IsReady);
        Assert.Equal(100, ready.ProgressPercent);
        Assert.Equal(TimeSpan.Zero, ready.RemainingDuration);
    }

    [Fact]
    public void Observe_InvalidFrameImmediatelyResetsContinuousConfirmation()
    {
        var monitor = new FaceReadinessMonitor(TimeSpan.FromSeconds(1.5), Frequency);
        monitor.Observe(meetsRequirements: true, timestamp: 1000);
        monitor.Observe(meetsRequirements: true, timestamp: 2000);

        var reset = monitor.Observe(meetsRequirements: false, timestamp: 2100);
        var restarted = monitor.Observe(meetsRequirements: true, timestamp: 2200);

        Assert.Equal(FaceReadinessState.NotReady, reset.State);
        Assert.Equal(0, reset.ProgressPercent);
        Assert.Equal(FaceReadinessState.Stabilizing, restarted.State);
        Assert.Equal(0, restarted.ProgressPercent);
    }
}
