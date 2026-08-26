namespace RuinaoSoftwareWpf;

using OpenCvSharp;
using System.Globalization;

/// <summary>
/// Debug 诊断叠加只画在预览副本上，不进入录像帧，也不参与人脸判定。
/// </summary>
internal static class CameraFaceDiagnosticRenderer
{
    public static void Draw(Mat frame, CameraFaceAnalysis analysis)
    {
        if (analysis.DetectorFaceBounds is { } detector)
        {
            Cv2.Rectangle(frame, Denormalize(detector, frame), new Scalar(255, 120, 40), 1);
        }

        if (analysis.LandmarkInputBounds is { } input)
        {
            Cv2.Rectangle(frame, Denormalize(input, frame), new Scalar(0, 180, 255), 1);
        }

        if (analysis.Landmarks is { Count: > 0 } landmarks)
        {
            foreach (var landmark in landmarks)
            {
                var point = new Point(
                    Math.Clamp((int)Math.Round(landmark.X * frame.Width), 0, frame.Width - 1),
                    Math.Clamp((int)Math.Round(landmark.Y * frame.Height), 0, frame.Height - 1));
                Cv2.Circle(frame, point, LandmarkRadius(landmark.Index), LandmarkColor(landmark.Index), -1);
                if (landmark.Index is 0 or 8 or 16 or 24 or 32 or 60 or 68 or 76)
                {
                    Cv2.PutText(
                        frame,
                        landmark.Index.ToString(CultureInfo.InvariantCulture),
                        new Point(point.X + 3, point.Y - 3),
                        HersheyFonts.HersheySimplex,
                        0.30,
                        Scalar.White,
                        1,
                        LineTypes.AntiAlias);
                }
            }
        }

        var baseline = analysis.OpenEyeBaseline?.ToString("0.000", CultureInfo.InvariantCulture) ?? "learning";
        var threshold = analysis.ClosedEyeThreshold?.ToString("0.000", CultureInfo.InvariantCulture) ?? "n/a";
        var leftEye = analysis.LeftEyeAspectRatio?.ToString("0.000", CultureInfo.InvariantCulture) ?? "n/a";
        var rightEye = analysis.RightEyeAspectRatio?.ToString("0.000", CultureInfo.InvariantCulture) ?? "n/a";
        Cv2.PutText(
            frame,
            $"98PT  EAR={leftEye}/{rightEye}  BASE={baseline}  TH={threshold}",
            new Point(10, 22),
            HersheyFonts.HersheySimplex,
            0.46,
            new Scalar(90, 240, 255),
            1,
            LineTypes.AntiAlias);
    }

    private static Rect Denormalize(NormalizedCameraRect rect, Mat frame) => new(
        (int)Math.Round(rect.X * frame.Width),
        (int)Math.Round(rect.Y * frame.Height),
        Math.Max(1, (int)Math.Round(rect.Width * frame.Width)),
        Math.Max(1, (int)Math.Round(rect.Height * frame.Height)));

    private static int LandmarkRadius(int index) => index is >= 11 and <= 21 ? 3 : 2;

    private static Scalar LandmarkColor(int index) => index switch
    {
        >= 0 and <= 32 => new Scalar(0, 220, 255),
        >= 60 and <= 75 => new Scalar(70, 255, 70),
        >= 76 and <= 95 => new Scalar(255, 80, 220),
        _ => new Scalar(255, 210, 80)
    };
}
