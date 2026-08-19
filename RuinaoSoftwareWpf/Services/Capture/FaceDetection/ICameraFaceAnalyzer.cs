namespace RuinaoSoftwareWpf;

using OpenCvSharp;

public interface ICameraFaceAnalyzer : IDisposable
{
    CameraFaceAnalysis Analyze(
        Mat frame,
        long sequence,
        long analyzedAtTimestamp,
        DateTimeOffset capturedAt);
}
