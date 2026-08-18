namespace RuinaoSoftwareWpf;

using RuinaoSoftwareWpf.ApplicationContracts;

/// <summary>
/// 评估入口的唯一应用服务：只读查询、创建新 Run 和继续既有 Run。
/// </summary>
public interface IAssessmentRunCoordinator
{
    Task<AssessmentRunContext?> GetActiveRunAsync(
        int totalModuleCount,
        CancellationToken cancellationToken = default);

    Task<AssessmentRunContext> CreateRunAsync(
        int totalModuleCount,
        CancellationToken cancellationToken = default);

    Task<AssessmentRunContext> ResumeRunAsync(
        long runId,
        int totalModuleCount,
        CancellationToken cancellationToken = default);
}
