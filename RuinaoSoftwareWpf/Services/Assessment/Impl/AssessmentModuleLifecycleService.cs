namespace RuinaoSoftwareWpf;

using RuinaoSoftwareWpf.ApplicationContracts;

/// <summary>
/// 患者评估批次与模块尝试的唯一应用层生命周期入口。
/// 具体任务计时和显示仍由各模块维护，是否允许开始及结果是否有效由这里决定。
/// </summary>
internal sealed class AssessmentModuleLifecycleService : IAssessmentModule
{
    private readonly IAssessmentRunStore store;
    private readonly IPatientService patientService;
    private readonly TimeProvider timeProvider;

    public AssessmentModuleLifecycleService(
        IAssessmentRunStore store,
        IPatientService patientService,
        TimeProvider timeProvider)
    {
        this.store = store;
        this.patientService = patientService;
        this.timeProvider = timeProvider;
    }

    public Task<AssessmentModuleRunContext> StartAsync(
        AssessmentModuleStartRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var currentPatientCode = patientService.CurrentPatient?.PatientCode;
        if (!string.Equals(currentPatientCode, request.PatientCode, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("当前患者已变化，不能启动该评估模块。");
        }

        if (request.ModuleIndex < 0 || request.ModuleIndex >= request.TotalModuleCount)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "模块序号超出正式评估流程范围。");
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(request.RunId);

        return store.StartModuleAsync(request, timeProvider.GetUtcNow(), cancellationToken);
    }

    public Task MarkSavingAsync(long attemptId, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(attemptId);
        return store.MarkSavingAsync(attemptId, timeProvider.GetUtcNow(), cancellationToken);
    }

    public Task<AssessmentModuleResult> CompleteAsync(
        long attemptId,
        string? resultJson = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(attemptId);
        return store.CompleteModuleAsync(attemptId, resultJson, timeProvider.GetUtcNow(), cancellationToken);
    }

    public Task<AssessmentModuleResult> CancelAsync(
        long attemptId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(attemptId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return store.CancelModuleAsync(attemptId, reason, timeProvider.GetUtcNow(), cancellationToken);
    }

    public Task<AssessmentModuleResult> FailAsync(
        long attemptId,
        string errorCode,
        string message,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(attemptId);
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        return store.FailModuleAsync(attemptId, errorCode, message, timeProvider.GetUtcNow(), cancellationToken);
    }
}
