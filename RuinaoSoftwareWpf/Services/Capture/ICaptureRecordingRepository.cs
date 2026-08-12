namespace RuinaoSoftwareWpf;

/// <summary>
/// 采集工作台本地记录仓储。
/// 这里仅保存采集业务数据、模块事件和外部设备采样数据。
/// 普通运行日志由 ILoggingService 保存；正式安全审计由 IAuditTrailService 写入独立数据库。
/// </summary>
public interface ICaptureRecordingRepository
{
    Task<CaptureSessionInfo> CreateModuleSessionAsync(
        string outputRoot,
        long? assessmentAttemptId,
        string sessionKey,
        string moduleCode,
        string moduleName,
        string cameraName,
        string rawVideoPath,
        string normalizedVideoPath,
        string audioPath,
        string mergedVideoPath,
        CancellationToken cancellationToken = default);

    Task CompleteSessionAsync(
        CaptureSessionInfo session,
        string status,
        string? message = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 记录模块内事件。
    /// 事件可以是演示开始、面部取景通过、图片显示、视频播放、表单提交等业务时间点。
    /// event_time_unix_ms 用于后续和 EEG、血氧、电刺激按误差窗口对齐。
    /// </summary>
    Task RecordModuleEventAsync(
        CaptureSessionInfo session,
        string eventType,
        string? message = null,
        string? payloadJson = null,
        DateTimeOffset? startedAt = null,
        DateTimeOffset? endedAt = null,
        CancellationToken cancellationToken = default);

    /// <summary>创建或保存一次表单型模块记录。</summary>
    Task<CaptureFormRecordInfo> SaveFormModuleRecordAsync(
        string outputRoot,
        long assessmentAttemptId,
        string sessionKey,
        string moduleCode,
        string moduleName,
        string formPayloadJson,
        string status = "completed",
        CancellationToken cancellationToken = default);

}
