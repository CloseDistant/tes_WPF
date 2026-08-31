namespace RuinaoSoftwareWpf.Tests;

using OpenCvSharp;
using Xunit;

public sealed class OpenVinoCameraFaceAnalyzerTests
{
    [Fact]
    public void CalculateLandmarkInputBounds_IncludesSpaceForActualChinDetection()
    {
        var detectorBounds = new Rect(100, 50, 100, 100);

        var result = OpenVinoCameraFaceAnalyzer.CalculateLandmarkInputBounds(
            detectorBounds,
            frameWidth: 640,
            frameHeight: 480);

        Assert.Equal(new Rect(94, 46, 112, 132), result);
        Assert.True(result.Bottom > detectorBounds.Bottom);
    }

    [Fact]
    public void CalculateLandmarkInputBounds_ClampsExpandedAreaToFrame()
    {
        var detectorBounds = new Rect(2, 1, 100, 100);

        var result = OpenVinoCameraFaceAnalyzer.CalculateLandmarkInputBounds(
            detectorBounds,
            frameWidth: 105,
            frameHeight: 115);

        Assert.Equal(0, result.Left);
        Assert.Equal(0, result.Top);
        Assert.Equal(105, result.Right);
        Assert.Equal(115, result.Bottom);
    }

    [Fact]
    public void CalculateDetectedFaceBounds_ExtendsBottomToDetectedChin()
    {
        var detectorBounds = new Rect(100, 50, 100, 100);
        var landmarks = Enumerable
            .Range(0, 98)
            .Select(_ => new FaceLandmarkPoint(0, 0, 0))
            .ToArray();
        landmarks[15] = new FaceLandmarkPoint(145, 160, 1);
        landmarks[16] = new FaceLandmarkPoint(150, 170, 1);
        landmarks[17] = new FaceLandmarkPoint(155, 160, 1);

        var result = OpenVinoCameraFaceAnalyzer.CalculateDetectedFaceBounds(
            detectorBounds,
            landmarks,
            frameWidth: 640,
            frameHeight: 480,
            minimumConfidence: 0.08);

        Assert.True(result.Bottom > 170);
        Assert.True(result.Bottom > detectorBounds.Bottom);
    }

    [Fact]
    public void CalculateDetectedFaceBounds_DoesNotUseDetectorBottomAsFixedExtension()
    {
        var detectorBounds = new Rect(100, 50, 100, 160);
        var landmarks = Enumerable
            .Range(0, 98)
            .Select(_ => new FaceLandmarkPoint(0, 0, 0))
            .ToArray();
        landmarks[15] = new FaceLandmarkPoint(145, 160, 1);
        landmarks[16] = new FaceLandmarkPoint(150, 170, 1);
        landmarks[17] = new FaceLandmarkPoint(155, 160, 1);

        var result = OpenVinoCameraFaceAnalyzer.CalculateDetectedFaceBounds(
            detectorBounds,
            landmarks,
            frameWidth: 640,
            frameHeight: 480,
            minimumConfidence: 0.08);

        Assert.True(result.Bottom > 170);
        Assert.True(result.Bottom < detectorBounds.Bottom);
    }

    [Fact]
    public void CalculateDetectedFaceBounds_RequiresReliableCentralChinPoints()
    {
        var detectorBounds = new Rect(100, 50, 100, 100);
        var landmarks = Enumerable
            .Range(0, 98)
            .Select(_ => new FaceLandmarkPoint(0, 0, 0))
            .ToArray();
        landmarks[11] = new FaceLandmarkPoint(130, 140, 1);
        landmarks[12] = new FaceLandmarkPoint(135, 145, 1);
        landmarks[15] = new FaceLandmarkPoint(145, 165, 1);

        var result = OpenVinoCameraFaceAnalyzer.CalculateDetectedFaceBounds(
            detectorBounds,
            landmarks,
            frameWidth: 640,
            frameHeight: 480,
            minimumConfidence: 0.08);

        Assert.Equal(detectorBounds, result);
    }

    [Fact]
    public void Analyze_LoadsBundledModelsAndReportsNoFaceForBlankFrame()
    {
        var logger = new RecordingLoggingService();
        using var analyzer = new OpenVinoCameraFaceAnalyzer(logger);
        using var frame = new Mat(new Size(640, 480), MatType.CV_8UC3, Scalar.Black);

        var result = analyzer.Analyze(
            frame,
            sequence: 1,
            analyzedAtTimestamp: 100,
            capturedAt: DateTimeOffset.UtcNow);

        Assert.True(
            result.State == CameraFaceState.NoFace,
            $"Expected NoFace, actual {result.State}. Last error: {logger.LastError}");
        Assert.Equal(0, result.FaceCount);
    }

    [Fact]
    public void Analyze_RunsFullQualityPipelineForBundledHumanFaceImage()
    {
        var logger = new RecordingLoggingService();
        using var analyzer = new OpenVinoCameraFaceAnalyzer(logger);
        var imagePath = Path.Combine(
            AppContext.BaseDirectory,
            "Assets",
            "CaptureWorkbench",
            "EmotionStroop",
            "fxmk98454s108.png");
        using var frame = Cv2.ImRead(imagePath);

        var result = analyzer.Analyze(
            frame,
            sequence: 1,
            analyzedAtTimestamp: 100,
            capturedAt: DateTimeOffset.UtcNow);

        Assert.True(
            result.FaceCount == 1,
            $"Expected one face, actual state={result.State}, count={result.FaceCount}. Last error: {logger.LastError}");
        Assert.NotEqual(CameraFaceState.DetectorUnavailable, result.State);
        Assert.NotNull(result.YawDegrees);
        Assert.NotNull(result.LeftEyeAspectRatio);
        Assert.NotNull(result.DetectorFaceBounds);
        Assert.NotNull(result.LandmarkInputBounds);
        Assert.Equal(98, result.Landmarks?.Count);
        Assert.NotNull(result.ClosedEyeThreshold);
    }

    private sealed class RecordingLoggingService : ILoggingService
    {
        public string CurrentLogPath => string.Empty;
        public string? LastError { get; private set; }
        public void Debug(string message) { }
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message, Exception? exception = null) =>
            LastError = $"{message} {exception}";
        public void Hardware(string message) { }
        public void HardwareTx(string command, byte[] frame) { }
        public void HardwareRx(string source, byte[] frame) { }
        public void HardwareDecision(string message) { }
    }
}
