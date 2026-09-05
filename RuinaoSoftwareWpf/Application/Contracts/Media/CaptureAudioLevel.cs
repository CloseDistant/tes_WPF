namespace RuinaoSoftwareWpf.ApplicationContracts;

/// <summary>
/// 麦克风短窗口音量摘要。仅用于判断是否存在声音，不包含语音内容。
/// </summary>
public sealed record CaptureAudioLevel(
    DateTimeOffset CapturedAt,
    double Rms,
    double Peak,
    int BytesRecorded);
