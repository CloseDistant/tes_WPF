namespace RuinaoSoftwareWpf;

/// <summary>
/// 表单型模块记录信息。
/// 后续个人基本信息和 A-J 问卷提交时使用，不强依赖音视频文件。
/// </summary>
public sealed record CaptureFormRecordInfo(
    long WorkbenchSessionId,
    long ModuleRecordId,
    long AssessmentAttemptId,
    string SessionKey,
    string ModuleCode,
    string ModuleName,
    string DatabasePath);
