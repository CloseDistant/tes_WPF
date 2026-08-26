namespace RuinaoSoftwareWpf.Tests;

using Xunit;

public sealed class AdaptiveEyeOpennessBaselineTests
{
    [Fact]
    public void Observe_LearnsPersonalOpenEyeBaselineAndUsesRelativeThreshold()
    {
        var baseline = new AdaptiveEyeOpennessBaseline();

        for (var index = 0; index < 8; index++)
        {
            baseline.Observe(new FaceQualityEvaluation(
                CameraFaceState.Normal,
                0.24 + index * 0.002,
                0.26 + index * 0.002,
                baseline.ClosedEyeThreshold));
        }

        Assert.NotNull(baseline.OpenEyeBaseline);
        Assert.InRange(baseline.OpenEyeBaseline!.Value, 0.25, 0.28);
        Assert.InRange(baseline.ClosedEyeThreshold, 0.13, 0.16);
    }

    [Fact]
    public void Observe_DoesNotLearnFromClosedOrAbnormalFrames()
    {
        var baseline = new AdaptiveEyeOpennessBaseline();

        for (var index = 0; index < 12; index++)
        {
            baseline.Observe(new FaceQualityEvaluation(
                CameraFaceState.EyesClosed,
                0.05,
                0.05,
                baseline.ClosedEyeThreshold));
        }

        Assert.Null(baseline.OpenEyeBaseline);
        Assert.Equal(0.10, baseline.ClosedEyeThreshold, 3);
    }

    [Fact]
    public void Reset_DiscardsPreviousPersonBaseline()
    {
        var baseline = new AdaptiveEyeOpennessBaseline();
        for (var index = 0; index < 8; index++)
        {
            baseline.Observe(new FaceQualityEvaluation(
                CameraFaceState.Normal,
                0.28,
                0.28,
                baseline.ClosedEyeThreshold));
        }

        baseline.Reset();

        Assert.Null(baseline.OpenEyeBaseline);
        Assert.Equal(0.10, baseline.ClosedEyeThreshold, 3);
    }
}
