namespace RuinaoSoftwareWpf;

using OpenCvSharp;

public interface ICameraFaceAnalyzer : IDisposable
{
    void Reset();

    CameraFaceAnalysis Analyze(
        Mat frame,
        long sequence,
        long analyzedAtTimestamp,
        DateTimeOffset capturedAt);
}
