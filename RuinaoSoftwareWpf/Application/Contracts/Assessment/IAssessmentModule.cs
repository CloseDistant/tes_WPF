namespace RuinaoSoftwareWpf.ApplicationContracts;

public enum AssessmentModuleKind
{
    EyeCalibration,
    PictureBrowse,
    VideoBrowse,
    VoiceBaseline,
    WordReading,
    ShortTextReading,
    EmotionQuestion,
    DotProbe,
    EmotionOddball,
    EmotionLetterSearch,
    EmotionStroop,
    Questionnaire,
    BasicInformation,
    GenericTask,
    SynchronizationTest
}

public sealed record AssessmentModuleDefinition(
    string Code,
    string DisplayNameKey,
    AssessmentModuleKind Kind,
    bool IsDevelopmentOnly);

/// <summary>
/// 模块静态说明。只用于界面展示和模块目录，不承载运行生命周期。
/// </summary>
public interface IAssessmentModuleDescriptor
{
    AssessmentModuleDefinition Definition { get; }
}

public enum AssessmentRunStatus
{
    InProgress,
    Completed
}

public enum AssessmentModuleExecutionStatus
{
    Running,
    Saving,
    Completed,
    CancelledInvalid,
    Failed
}

public sealed record AssessmentModuleStartRequest(
    string PatientCode,
    string SessionKey,
    string ModuleCode,
    string ModuleName,
    int ModuleIndex,
    int TotalModuleCount);

public sealed record AssessmentModuleRunContext(
    long RunId,
    long AttemptId,
    int AttemptNumber,
    string PatientCode,
    string SessionKey,
    string ModuleCode,
    string ModuleName,
    int ModuleIndex,
    DateTimeOffset StartedAt);

public sealed record AssessmentModuleResult(
    long RunId,
    long AttemptId,
    AssessmentModuleExecutionStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    string? ErrorCode,
    string? Message);

public sealed record AssessmentProgressSnapshot(
    long? RunId,
    string PatientCode,
    AssessmentRunStatus Status,
    int NextModuleIndex,
    IReadOnlyList<string> CompletedModuleCodes);

/// <summary>
/// 正式评估模块生命周期入口。
/// ViewModel 只能通过该契约创建、完成、取消或失败一次模块尝试。
/// </summary>
public interface IAssessmentModule
{
    Task<AssessmentProgressSnapshot> GetProgressAsync(
        string patientCode,
        int totalModuleCount,
        CancellationToken cancellationToken = default);

    Task<AssessmentModuleRunContext> StartAsync(
        AssessmentModuleStartRequest request,
        CancellationToken cancellationToken = default);

    Task MarkSavingAsync(
        long attemptId,
        CancellationToken cancellationToken = default);

    Task<AssessmentModuleResult> CompleteAsync(
        long attemptId,
        string? resultJson = null,
        CancellationToken cancellationToken = default);

    Task<AssessmentModuleResult> CancelAsync(
        long attemptId,
        string reason,
        CancellationToken cancellationToken = default);

    Task<AssessmentModuleResult> FailAsync(
        long attemptId,
        string errorCode,
        string message,
        CancellationToken cancellationToken = default);
}
