namespace RuinaoSoftwareWpf.Tests;

using Xunit;

public sealed class FaceConditionMonitorTests
{
    private const long Frequency = 1000;

    [Fact]
    public void Observe_ConfirmsContinuousAbnormalStateAfterThreeSecondsOnlyOnce()
    {
        var monitor = new FaceConditionMonitor(TimeSpan.FromSeconds(3), Frequency);

        Assert.False(monitor.Observe(CameraFaceState.NoFace, 1000).JustConfirmed);
        Assert.False(monitor.Observe(CameraFaceState.NoFace, 3999).JustConfirmed);

        var confirmed = monitor.Observe(CameraFaceState.NoFace, 4000);

        Assert.True(confirmed.JustConfirmed);
        Assert.Equal(TimeSpan.FromSeconds(3), confirmed.AbnormalDuration);
        Assert.False(monitor.Observe(CameraFaceState.NoFace, 5000).JustConfirmed);
    }

    [Fact]
    public void Observe_ChangingAbnormalReasonDoesNotRestartCountdown()
    {
        var monitor = new FaceConditionMonitor(TimeSpan.FromSeconds(3), Frequency);

        monitor.Observe(CameraFaceState.NoFace, 1000);
        monitor.Observe(CameraFaceState.MultipleFaces, 2500);
        var confirmed = monitor.Observe(CameraFaceState.HeadPoseInvalid, 4000);

        Assert.True(confirmed.JustConfirmed);
        Assert.Equal(TimeSpan.FromSeconds(3), confirmed.AbnormalDuration);
    }

    [Fact]
    public void Observe_NormalStateResetsAbnormalCountdown()
    {
        var monitor = new FaceConditionMonitor(TimeSpan.FromSeconds(3), Frequency);

        monitor.Observe(CameraFaceState.EyesClosed, 1000);
        monitor.Observe(CameraFaceState.Normal, 3500);
        var restarted = monitor.Observe(CameraFaceState.EyesClosed, 4000);

        Assert.False(restarted.JustConfirmed);
        Assert.Equal(TimeSpan.Zero, restarted.AbnormalDuration);
        Assert.False(monitor.Observe(CameraFaceState.EyesClosed, 6999).JustConfirmed);
        Assert.True(monitor.Observe(CameraFaceState.EyesClosed, 7000).JustConfirmed);
    }
}
