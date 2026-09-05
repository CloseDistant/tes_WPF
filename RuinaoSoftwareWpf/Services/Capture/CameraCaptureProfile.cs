namespace RuinaoSoftwareWpf;

/// <summary>
/// 所有摄像头统一使用的能力请求，不针对具体品牌或型号散布特殊参数。
/// 设备优先尝试用户所选档位；驱动无法完全协商时保留实际规格并继续采集，避免因能力报告偏差阻断流程。
/// </summary>
public sealed record CameraCaptureProfile(
    int RequestedWidth,
    int RequestedHeight,
    double DeviceFramesPerSecond,
    double PreviewFramesPerSecond,
    double RecordingFramesPerSecond,
    double FaceAnalysisFramesPerSecond,
    int PreviewMaximumWidth,
    string PreferredInputCodec,
    CameraRecordingQualityMode RecordingQualityMode)
{
    public static CameraCaptureProfile Preferred => ForMode(CameraRecordingQualityMode.Balanced);

    public static CameraCaptureProfile ForMode(CameraRecordingQualityMode mode) => mode switch
    {
        CameraRecordingQualityMode.Balanced => Create(mode, 1920, 1080, 30),
        CameraRecordingQualityMode.HighDefinition => Create(mode, 3840, 2160, 30),
        CameraRecordingQualityMode.HighFrameRate => Create(mode, 1920, 1080, 60),
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "未知的正式录像质量档位。")
    };

    private static CameraCaptureProfile Create(
        CameraRecordingQualityMode mode,
        int width,
        int height,
        double recordingFramesPerSecond) => new(
        RequestedWidth: width,
        RequestedHeight: height,
        DeviceFramesPerSecond: recordingFramesPerSecond,
        PreviewFramesPerSecond: 20,
        RecordingFramesPerSecond: recordingFramesPerSecond,
        FaceAnalysisFramesPerSecond: 5,
        PreviewMaximumWidth: 640,
        PreferredInputCodec: "MJPG",
        RecordingQualityMode: mode);

    public TimeSpan PreviewInterval => IntervalFor(PreviewFramesPerSecond);

    public TimeSpan RecordingInterval => IntervalFor(RecordingFramesPerSecond);

    public TimeSpan FaceAnalysisInterval => IntervalFor(FaceAnalysisFramesPerSecond);

    private static TimeSpan IntervalFor(double framesPerSecond)
    {
        if (!double.IsFinite(framesPerSecond) || framesPerSecond <= 0)
        {
            throw new InvalidOperationException("摄像头链路帧率必须大于 0。");
        }

        return TimeSpan.FromSeconds(1d / framesPerSecond);
    }
}

public enum CameraRecordingQualityMode
{
    Balanced,
    HighDefinition,
    HighFrameRate
}

public static class CameraRecordingQualityCatalog
{
    public static IReadOnlyList<CameraRecordingQualityMode> All { get; } =
    [
        CameraRecordingQualityMode.Balanced,
        CameraRecordingQualityMode.HighDefinition,
        CameraRecordingQualityMode.HighFrameRate
    ];

    public static string DisplayName(CameraRecordingQualityMode mode) => mode switch
    {
        CameraRecordingQualityMode.Balanced => "均衡模式",
        CameraRecordingQualityMode.HighDefinition => "高清模式",
        CameraRecordingQualityMode.HighFrameRate => "高帧模式",
        _ => mode.ToString()
    };

    public static string Specification(CameraRecordingQualityMode mode) => mode switch
    {
        CameraRecordingQualityMode.Balanced => "1920 × 1080 · 30 FPS",
        CameraRecordingQualityMode.HighDefinition => "3840 × 2160 · 30 FPS",
        CameraRecordingQualityMode.HighFrameRate => "1920 × 1080 · 60 FPS",
        _ => string.Empty
    };

    public static string Description(CameraRecordingQualityMode mode) => mode switch
    {
        CameraRecordingQualityMode.Balanced => "兼顾清晰度、稳定性和合成速度，建议默认使用",
        CameraRecordingQualityMode.HighDefinition => "优先保留面部空间细节，对磁盘和处理性能要求更高",
        CameraRecordingQualityMode.HighFrameRate => "优先保留动作和表情变化，对摄像头帧率要求更高",
        _ => string.Empty
    };

    public static string PerformanceNote(CameraRecordingQualityMode mode) => mode switch
    {
        CameraRecordingQualityMode.HighDefinition => "摄像头启动及音视频合成可能更慢",
        CameraRecordingQualityMode.HighFrameRate => "摄像头启动及音视频合成可能更慢",
        _ => string.Empty
    };
}

public sealed record CameraCaptureProfileSnapshot(
    int RequestedWidth,
    int RequestedHeight,
    double RequestedDeviceFramesPerSecond,
    double PreviewFramesPerSecond,
    double RecordingFramesPerSecond,
    double FaceAnalysisFramesPerSecond,
    string PreferredInputCodec,
    int ActualWidth,
    int ActualHeight,
    double? ActualDeviceFramesPerSecond,
    string? ActualInputCodec,
    string CaptureBackend,
    CameraRecordingQualityMode RecordingQualityMode,
    bool UsesDriverDefault = false,
    double? OpenToFirstFrameMilliseconds = null,
    double? MeasuredSourceFramesPerSecond = null,
    double? MaximumSourceFrameGapMilliseconds = null);

/// <summary>
/// 经真实设备打开验证过的摄像头启动偏好。正式页面优先复用已验证偏好，
/// 不在患者等待期间遍历分辨率、编码格式和后端组合。
/// </summary>
public sealed record CameraOpeningPreference(
    string DeviceKey,
    string CaptureBackend,
    bool UsesDriverDefault,
    int Width,
    int Height,
    double? FramesPerSecond,
    string? InputCodec,
    double? MeasuredSourceFramesPerSecond,
    DateTimeOffset VerifiedAt,
    CameraRecordingQualityMode RecordingQualityMode = CameraRecordingQualityMode.Balanced);
