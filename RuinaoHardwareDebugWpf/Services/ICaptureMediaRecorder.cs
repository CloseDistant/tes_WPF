namespace RuinaoHardwareDebugWpf;

using OpenCvSharp;

/// <summary>
/// 启动一次采集音视频录制所需的模块上下文。
/// View 层只提供当前模块和摄像头信息，具体文件路径和数据库记录由录制服务生成。
/// </summary>
public sealed record CaptureRecordingRequest(
    string OutputRoot,
    string SessionKey,
    string ModuleCode,
    string ModuleName,
    string CameraName);

/// <summary>
/// 采集录制结束后的结果。
/// completed 表示已合成可用视频；discarded 表示中断后丢弃；merge_failed 表示合成失败。
/// </summary>
public sealed record CaptureRecordingCompletedEventArgs(
    CaptureSessionInfo Session,
    string Status,
    string Message);

/// <summary>
/// 采集工作台音视频录制服务。
/// 负责录制摄像头帧、麦克风音频、调用 FFmpeg 合成，以及把会话状态写入本地仓储。
/// </summary>
public interface ICaptureMediaRecorder
{
    event EventHandler<CaptureRecordingCompletedEventArgs>? RecordingCompleted;

    bool IsRecording { get; }

    string? CurrentModuleName { get; }

    CaptureSessionInfo? CurrentSession { get; }

    Task<CaptureSessionInfo> StartAsync(CaptureRecordingRequest request, CancellationToken cancellationToken = default);

    int RecordFrame(Mat frame);

    Task RecordModuleEventAsync(
        string eventType,
        string? message = null,
        string? payloadJson = null,
        DateTimeOffset? startedAt = null,
        DateTimeOffset? endedAt = null,
        CancellationToken cancellationToken = default);

    Task RecordModuleEventAsync(
        CaptureSessionInfo session,
        string eventType,
        string? message = null,
        string? payloadJson = null,
        DateTimeOffset? startedAt = null,
        DateTimeOffset? endedAt = null,
        CancellationToken cancellationToken = default);

    Task<CaptureFormRecordInfo> SaveFormModuleRecordAsync(
        string outputRoot,
        string sessionKey,
        string moduleCode,
        string moduleName,
        string formPayloadJson,
        string status = "completed",
        CancellationToken cancellationToken = default);

    void RequestStop(string status, string message);

    Task WaitForIdleAsync(CancellationToken cancellationToken = default);
}
