namespace RuinaoSoftwareWpf.Tests;

using Xunit;

public sealed class CameraCaptureProfileTests
{
    [Fact]
    public void Preferred_UsesUnified1080pAndIndependentTwentyThirtyFivePipelines()
    {
        var profile = CameraCaptureProfile.Preferred;

        Assert.Equal(1920, profile.RequestedWidth);
        Assert.Equal(1080, profile.RequestedHeight);
        Assert.Equal(30, profile.DeviceFramesPerSecond);
        Assert.Equal(20, profile.PreviewFramesPerSecond);
        Assert.Equal(30, profile.RecordingFramesPerSecond);
        Assert.Equal(5, profile.FaceAnalysisFramesPerSecond);
        Assert.Equal(TimeSpan.FromSeconds(1d / 20d), profile.PreviewInterval);
        Assert.Equal(TimeSpan.FromSeconds(1d / 30d), profile.RecordingInterval);
        Assert.Equal(TimeSpan.FromMilliseconds(200), profile.FaceAnalysisInterval);
    }

    [Theory]
    [InlineData(CameraRecordingQualityMode.Balanced, 1920, 1080, 30)]
    [InlineData(CameraRecordingQualityMode.HighDefinition, 3840, 2160, 30)]
    [InlineData(CameraRecordingQualityMode.HighFrameRate, 1920, 1080, 60)]
    public void ForMode_MapsAdvancedSettingToUnifiedCaptureProfile(
        CameraRecordingQualityMode mode,
        int expectedWidth,
        int expectedHeight,
        double expectedFramesPerSecond)
    {
        var profile = CameraCaptureProfile.ForMode(mode);

        Assert.Equal(expectedWidth, profile.RequestedWidth);
        Assert.Equal(expectedHeight, profile.RequestedHeight);
        Assert.Equal(expectedFramesPerSecond, profile.DeviceFramesPerSecond);
        Assert.Equal(expectedFramesPerSecond, profile.RecordingFramesPerSecond);
        Assert.Equal(mode, profile.RecordingQualityMode);
        Assert.Equal(20, profile.PreviewFramesPerSecond);
        Assert.Equal(5, profile.FaceAnalysisFramesPerSecond);
        Assert.Equal(640, profile.PreviewMaximumWidth);
        Assert.Equal("MJPG", profile.PreferredInputCodec);
    }

    [Theory]
    [InlineData(3840, 2160, 16)]
    [InlineData(1920, 1080, 64)]
    [InlineData(1280, 720, 90)]
    public void FrameQueueCapacity_UsesResolutionAwareMemoryBudget(
        int width,
        int height,
        int expectedCapacity)
    {
        var profile = new CameraCaptureProfileSnapshot(
            width,
            height,
            20,
            30,
            30,
            5,
            "MJPG",
            width,
            height,
            30,
            "MJPG",
            "DSHOW",
            CameraRecordingQualityMode.Balanced);

        Assert.Equal(expectedCapacity, CaptureMediaRecorder.CalculateFrameQueueCapacity(profile));
    }

    [Fact]
    public void JsonProfileStore_RoundTripsVerifiedDevicePreference()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ruinao-camera-profile-{Guid.NewGuid():N}");
        var path = Path.Combine(root, "camera-capabilities.json");
        try
        {
            var store = new JsonCameraCaptureProfileStore(new NullLoggingService(), path);
            var expected = new CameraOpeningPreference(
                "camera-a",
                "DSHOW",
                UsesDriverDefault: true,
                Width: 1280,
                Height: 720,
                FramesPerSecond: 30,
                InputCodec: "MJPG",
                MeasuredSourceFramesPerSecond: 29.8,
                VerifiedAt: DateTimeOffset.Parse("2026-08-25T10:00:00Z"));

            store.Save(expected);

            Assert.Equal(
                expected,
                store.Find("CAMERA-A", CameraRecordingQualityMode.Balanced));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void JsonProfileStore_KeepsOpeningPreferenceForEachRecordingQualityMode()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ruinao-camera-profile-{Guid.NewGuid():N}");
        var path = Path.Combine(root, "camera-capabilities.json");
        try
        {
            var store = new JsonCameraCaptureProfileStore(new NullLoggingService(), path);
            var balanced = CreatePreference(
                CameraRecordingQualityMode.Balanced,
                "DSHOW",
                measuredFramesPerSecond: 29.8);
            var highDefinition = CreatePreference(
                CameraRecordingQualityMode.HighDefinition,
                "MSMF",
                measuredFramesPerSecond: 18.5);

            store.Save(balanced);
            store.Save(highDefinition);

            Assert.Equal(
                balanced,
                store.Find("camera-a", CameraRecordingQualityMode.Balanced));
            Assert.Equal(
                highDefinition,
                store.Find("camera-a", CameraRecordingQualityMode.HighDefinition));
            Assert.Null(store.Find("camera-a", CameraRecordingQualityMode.HighFrameRate));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void JsonProfileStore_InfersModeForLegacyPreferenceWithoutModeField()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ruinao-camera-profile-{Guid.NewGuid():N}");
        var path = Path.Combine(root, "camera-capabilities.json");
        try
        {
            Directory.CreateDirectory(root);
            var legacyPreference = new
            {
                DeviceKey = "camera-a",
                CaptureBackend = "MSMF",
                UsesDriverDefault = false,
                Width = 3840,
                Height = 2160,
                FramesPerSecond = 30d,
                InputCodec = "MJPG",
                MeasuredSourceFramesPerSecond = 18.5d,
                VerifiedAt = DateTimeOffset.Parse("2026-08-25T10:00:00Z")
            };
            File.WriteAllText(
                path,
                System.Text.Json.JsonSerializer.Serialize(new[] { legacyPreference }));
            var store = new JsonCameraCaptureProfileStore(new NullLoggingService(), path);

            Assert.Null(store.Find("camera-a", CameraRecordingQualityMode.Balanced));
            Assert.Equal(
                CameraRecordingQualityMode.HighDefinition,
                store.Find("camera-a", CameraRecordingQualityMode.HighDefinition)?.RecordingQualityMode);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static CameraOpeningPreference CreatePreference(
        CameraRecordingQualityMode mode,
        string backend,
        double measuredFramesPerSecond) => new(
        "camera-a",
        backend,
        UsesDriverDefault: false,
        Width: CameraCaptureProfile.ForMode(mode).RequestedWidth,
        Height: CameraCaptureProfile.ForMode(mode).RequestedHeight,
        FramesPerSecond: CameraCaptureProfile.ForMode(mode).DeviceFramesPerSecond,
        InputCodec: "MJPG",
        MeasuredSourceFramesPerSecond: measuredFramesPerSecond,
        VerifiedAt: DateTimeOffset.Parse("2026-08-25T10:00:00Z"),
        RecordingQualityMode: mode);

    private sealed class NullLoggingService : ILoggingService
    {
        public string CurrentLogPath => string.Empty;
        public void Debug(string message) { }
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message, Exception? exception = null) { }
        public void Hardware(string message) { }
        public void HardwareTx(string command, byte[] frame) { }
        public void HardwareRx(string source, byte[] frame) { }
        public void HardwareDecision(string message) { }
    }

}
