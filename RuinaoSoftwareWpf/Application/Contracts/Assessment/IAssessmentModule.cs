namespace RuinaoSoftwareWpf.ApplicationContracts;

/// <summary>
/// 正式评估模块生命周期入口。
/// ViewModel 只能通过该契约创建、完成、取消或失败一次模块尝试。
/// </summary>
public interface IAssessmentModule
{
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
