namespace RuinaoHardwareDebugWpf;

/// <summary>
/// 一次采集录像会话的本地记录信息。
/// View 层负责采集音视频文件，仓储层负责把会话元数据写入 SQLite。
/// </summary>
public sealed record CaptureSessionInfo(
    long Id,
    long WorkbenchSessionId,
    long ModuleRecordId,
    string SessionKey,
    string ModuleCode,
    string ModuleName,
    string DatabasePath,
    string OutputDirectory,
    string RawVideoPath,
    string NormalizedVideoPath,
    string AudioPath,
    string MergedVideoPath);

/// <summary>
/// 表单型模块记录信息。
/// 后续个人基本信息和 A-J 问卷提交时使用，不强依赖音视频文件。
/// </summary>
public sealed record CaptureFormRecordInfo(
    long WorkbenchSessionId,
    long ModuleRecordId,
    string SessionKey,
    string ModuleCode,
    string ModuleName,
    string DatabasePath);

/// <summary>
/// 采集工作台模块类型。
/// task：前半段包含图片、视频、语音、眼动等任务型采集。
/// form：情绪 Stroop 之后的个人信息和问卷表单提交。
/// </summary>
public static class CaptureModuleTypes
{
    public const string Task = "task";

    public const string Form = "form";
}

/// <summary>
/// 采集工作台本地记录仓储。
/// 这里仅保存采集业务数据、模块事件和外部设备采样数据。
/// 普通运行日志和审计日志统一走 ILoggingService，不写入 SQLite。
/// </summary>
public interface ICaptureRecordingRepository
{
    Task<CaptureSessionInfo> CreateModuleSessionAsync(
        string outputRoot,
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
        string sessionKey,
        string moduleCode,
        string moduleName,
        string formPayloadJson,
        string status = "completed",
        CancellationToken cancellationToken = default);

}
