namespace RuinaoSoftwareWpf;

public enum CameraFaceState
{
    DetectorUnavailable,
    NoFace,
    MultipleFaces,
    FaceOccluded,
    EyesNotVisible,
    EyesClosed,
    MouthNotVisible,
    HeadPoseInvalid,
    Normal
}

public sealed record CameraFaceAnalysis(
    long Sequence,
    long AnalyzedAtTimestamp,
    DateTimeOffset CapturedAt,
    CameraFaceState State,
    int FaceCount,
    NormalizedCameraRect? PrimaryFaceBounds,
    double? YawDegrees = null,
    double? PitchDegrees = null,
    double? RollDegrees = null,
    double? LeftEyeAspectRatio = null,
    double? RightEyeAspectRatio = null)
{
    public static CameraFaceAnalysis Unavailable(
        long sequence,
        long analyzedAtTimestamp,
        DateTimeOffset capturedAt) => new(
            sequence,
            analyzedAtTimestamp,
            capturedAt,
            CameraFaceState.DetectorUnavailable,
            0,
            null);
}

public readonly record struct FaceLandmarkPoint(
    double X,
    double Y,
    double Confidence);

internal static class FaceLandmarkIndices
{
    public static IReadOnlyList<int> FaceContour { get; } = Enumerable.Range(0, 33).ToArray();
    public static IReadOnlyList<int> Chin { get; } = [11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21];
}

public sealed record FaceQualityObservation(
    IReadOnlyList<FaceLandmarkPoint> Landmarks,
    double YawDegrees,
    double PitchDegrees,
    double RollDegrees);

public sealed record FaceQualityThresholds(
    double LandmarkConfidence,
    double FeatureVisibleRatio,
    double OverallVisibleRatio,
    double ClosedEyeAspectRatio,
    double MaximumYawDegrees,
    double MaximumPitchDegrees,
    double MaximumRollDegrees)
{
    public static FaceQualityThresholds Default { get; } = new(
        LandmarkConfidence: 0.08,
        FeatureVisibleRatio: 0.70,
        OverallVisibleRatio: 0.72,
        ClosedEyeAspectRatio: 0.16,
        MaximumYawDegrees: 20,
        MaximumPitchDegrees: 15,
        MaximumRollDegrees: 20);
}

public readonly record struct FaceQualityEvaluation(
    CameraFaceState State,
    double LeftEyeAspectRatio,
    double RightEyeAspectRatio);

/// <summary>
/// 将 98 点关键点和头部姿态转换为统一面部质量状态。
/// 阈值集中在 FaceQualityThresholds，便于真机校准。
/// </summary>
public sealed class FaceQualityEvaluator
{
    private static readonly int[] LeftEyeIndices = [60, 61, 62, 63, 64, 65, 66, 67];
    private static readonly int[] RightEyeIndices = [68, 69, 70, 71, 72, 73, 74, 75];
    private static readonly int[] NoseIndices = [51, 52, 53, 54, 55, 56, 57, 58, 59];
    private static readonly int[] MouthIndices =
        [76, 77, 78, 79, 80, 81, 82, 83, 84, 85, 86, 87, 88, 89, 90, 91, 92, 93, 94, 95];
    private static readonly int[] CentralFeatureIndices =
        [11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21,
         60, 61, 62, 63, 64, 65, 66, 67, 68, 69, 70, 71, 72, 73, 74, 75,
         51, 52, 53, 54, 55, 56, 57, 58, 59,
         76, 77, 78, 79, 80, 81, 82, 83, 84, 85, 86, 87, 88, 89, 90, 91, 92, 93, 94, 95];

    private readonly FaceQualityThresholds thresholds;

    public FaceQualityEvaluator(FaceQualityThresholds? thresholds = null)
    {
        this.thresholds = thresholds ?? FaceQualityThresholds.Default;
    }

    public FaceQualityEvaluation Evaluate(FaceQualityObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        if (observation.Landmarks.Count < 98)
        {
            return new FaceQualityEvaluation(CameraFaceState.FaceOccluded, 0, 0);
        }

        var leftEyeAspectRatio = CalculateEyeAspectRatio(observation.Landmarks, LeftEyeIndices);
        var rightEyeAspectRatio = CalculateEyeAspectRatio(observation.Landmarks, RightEyeIndices);

        if (VisibleRatio(observation.Landmarks, CentralFeatureIndices) < thresholds.OverallVisibleRatio)
        {
            return new FaceQualityEvaluation(
                CameraFaceState.FaceOccluded,
                leftEyeAspectRatio,
                rightEyeAspectRatio);
        }

        if (VisibleRatio(observation.Landmarks, FaceLandmarkIndices.Chin) < thresholds.FeatureVisibleRatio)
        {
            return new FaceQualityEvaluation(
                CameraFaceState.FaceOccluded,
                leftEyeAspectRatio,
                rightEyeAspectRatio);
        }

        if (VisibleRatio(observation.Landmarks, LeftEyeIndices) < thresholds.FeatureVisibleRatio
            || VisibleRatio(observation.Landmarks, RightEyeIndices) < thresholds.FeatureVisibleRatio)
        {
            return new FaceQualityEvaluation(
                CameraFaceState.EyesNotVisible,
                leftEyeAspectRatio,
                rightEyeAspectRatio);
        }

        if (VisibleRatio(observation.Landmarks, MouthIndices) < thresholds.FeatureVisibleRatio)
        {
            return new FaceQualityEvaluation(
                CameraFaceState.MouthNotVisible,
                leftEyeAspectRatio,
                rightEyeAspectRatio);
        }

        if (leftEyeAspectRatio < thresholds.ClosedEyeAspectRatio
            && rightEyeAspectRatio < thresholds.ClosedEyeAspectRatio)
        {
            return new FaceQualityEvaluation(
                CameraFaceState.EyesClosed,
                leftEyeAspectRatio,
                rightEyeAspectRatio);
        }

        if (Math.Abs(observation.YawDegrees) > thresholds.MaximumYawDegrees
            || Math.Abs(observation.PitchDegrees) > thresholds.MaximumPitchDegrees
            || Math.Abs(observation.RollDegrees) > thresholds.MaximumRollDegrees)
        {
            return new FaceQualityEvaluation(
                CameraFaceState.HeadPoseInvalid,
                leftEyeAspectRatio,
                rightEyeAspectRatio);
        }

        return new FaceQualityEvaluation(
            CameraFaceState.Normal,
            leftEyeAspectRatio,
            rightEyeAspectRatio);
    }

    private double VisibleRatio(IReadOnlyList<FaceLandmarkPoint> landmarks, IReadOnlyList<int> indices)
    {
        var visible = 0;
        for (var index = 0; index < indices.Count; index++)
        {
            if (landmarks[indices[index]].Confidence >= thresholds.LandmarkConfidence)
            {
                visible++;
            }
        }

        return visible / (double)indices.Count;
    }

    private static double CalculateEyeAspectRatio(
        IReadOnlyList<FaceLandmarkPoint> landmarks,
        IReadOnlyList<int> eye)
    {
        var width = Distance(landmarks[eye[0]], landmarks[eye[4]]);
        if (width <= double.Epsilon)
        {
            return 0;
        }

        var vertical = Distance(landmarks[eye[1]], landmarks[eye[7]])
            + Distance(landmarks[eye[2]], landmarks[eye[6]])
            + Distance(landmarks[eye[3]], landmarks[eye[5]]);
        return vertical / (3d * width);
    }

    private static double Distance(FaceLandmarkPoint left, FaceLandmarkPoint right)
    {
        var x = left.X - right.X;
        var y = left.Y - right.Y;
        return Math.Sqrt(x * x + y * y);
    }
}
