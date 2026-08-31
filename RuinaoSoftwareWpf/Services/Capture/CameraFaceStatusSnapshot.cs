namespace RuinaoSoftwareWpf;

/// <summary>
/// 与界面位图无关的人脸状态快照。正式采集期间即使关闭预览渲染，
/// 仍由独立的 5 FPS 人脸分析链路持续发布。
/// </summary>
public sealed record CameraFaceStatusSnapshot(
    long Sequence,
    DateTimeOffset CapturedAt,
    long AnalyzedAtTimestamp,
    CameraFaceState State,
    bool IsPrimaryFaceInsideGuide);
