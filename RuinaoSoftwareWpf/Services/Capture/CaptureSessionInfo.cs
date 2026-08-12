namespace RuinaoSoftwareWpf;

/// <summary>
/// 一次采集录像会话的本地记录信息。
/// View 层负责采集音视频文件，仓储层负责把会话元数据写入 SQLite。
/// </summary>
public sealed record CaptureSessionInfo(
    long Id,
    long WorkbenchSessionId,
    long ModuleRecordId,
    long? AssessmentAttemptId,
    string SessionKey,
    string ModuleCode,
    string ModuleName,
    string DatabasePath,
    string OutputDirectory,
    string RawVideoPath,
    string NormalizedVideoPath,
    string AudioPath,
    string MergedVideoPath);
