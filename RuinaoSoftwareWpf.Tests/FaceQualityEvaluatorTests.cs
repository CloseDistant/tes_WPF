namespace RuinaoSoftwareWpf.Tests;

using Xunit;

public sealed class FaceQualityEvaluatorTests
{
    private readonly FaceQualityEvaluator evaluator = new();

    [Fact]
    public void Evaluate_ReturnsNormalForVisibleFeaturesOpenEyesAndFrontalPose()
    {
        var result = evaluator.Evaluate(CreateObservation());

        Assert.Equal(CameraFaceState.Normal, result.State);
        Assert.True(result.LeftEyeAspectRatio > FaceQualityThresholds.Default.ClosedEyeAspectRatio);
        Assert.True(result.RightEyeAspectRatio > FaceQualityThresholds.Default.ClosedEyeAspectRatio);
    }

    [Fact]
    public void Evaluate_ReturnsFaceOccludedWhenCentralFeaturesAreMostlyUnavailable()
    {
        var landmarks = CreateLandmarks();
        SetConfidence(landmarks, 51, 95, 0);

        var result = evaluator.Evaluate(CreateObservation(landmarks));

        Assert.Equal(CameraFaceState.FaceOccluded, result.State);
    }

    [Fact]
    public void Evaluate_ReturnsEyesNotVisibleWhenOneEyeIsUnavailable()
    {
        var landmarks = CreateLandmarks();
        SetConfidence(landmarks, 60, 67, 0);

        var result = evaluator.Evaluate(CreateObservation(landmarks));

        Assert.Equal(CameraFaceState.EyesNotVisible, result.State);
    }

    [Fact]
    public void Evaluate_ReturnsMouthNotVisibleWhenMouthIsUnavailable()
    {
        var landmarks = CreateLandmarks();
        SetConfidence(landmarks, 76, 82, 0);

        var result = evaluator.Evaluate(CreateObservation(landmarks));

        Assert.Equal(CameraFaceState.MouthNotVisible, result.State);
    }

    [Fact]
    public void Evaluate_ReturnsEyesClosedWhenBothEyeAspectRatiosAreSmall()
    {
        var landmarks = CreateLandmarks(eyeHalfHeight: 0.10);

        var result = evaluator.Evaluate(CreateObservation(landmarks));

        Assert.Equal(CameraFaceState.EyesClosed, result.State);
    }

    [Theory]
    [InlineData(20.1, 0, 0)]
    [InlineData(0, -15.1, 0)]
    [InlineData(0, 0, 20.1)]
    public void Evaluate_ReturnsHeadPoseInvalidWhenAnyPoseLimitIsExceeded(
        double yaw,
        double pitch,
        double roll)
    {
        var result = evaluator.Evaluate(CreateObservation(yaw: yaw, pitch: pitch, roll: roll));

        Assert.Equal(CameraFaceState.HeadPoseInvalid, result.State);
    }

    private static FaceQualityObservation CreateObservation(
        IReadOnlyList<FaceLandmarkPoint>? landmarks = null,
        double yaw = 0,
        double pitch = 0,
        double roll = 0) => new(
            landmarks ?? CreateLandmarks(),
            yaw,
            pitch,
            roll);

    private static FaceLandmarkPoint[] CreateLandmarks(double eyeHalfHeight = 0.50)
    {
        var landmarks = Enumerable
            .Range(0, 98)
            .Select(index => new FaceLandmarkPoint(index, index, 1))
            .ToArray();
        SetEye(landmarks, 60, 0, eyeHalfHeight);
        SetEye(landmarks, 68, 10, eyeHalfHeight);
        return landmarks;
    }

    private static void SetEye(
        FaceLandmarkPoint[] landmarks,
        int startIndex,
        double xOffset,
        double halfHeight)
    {
        var points = new (double X, double Y)[]
        {
            (0, 0),
            (1, -halfHeight * 0.8),
            (2, -halfHeight),
            (3, -halfHeight * 0.8),
            (4, 0),
            (3, halfHeight * 0.8),
            (2, halfHeight),
            (1, halfHeight * 0.8)
        };

        for (var index = 0; index < points.Length; index++)
        {
            landmarks[startIndex + index] = new FaceLandmarkPoint(
                xOffset + points[index].X,
                points[index].Y,
                1);
        }
    }

    private static void SetConfidence(
        FaceLandmarkPoint[] landmarks,
        int firstIndex,
        int lastIndex,
        double confidence)
    {
        for (var index = firstIndex; index <= lastIndex; index++)
        {
            landmarks[index] = landmarks[index] with { Confidence = confidence };
        }
    }
}
