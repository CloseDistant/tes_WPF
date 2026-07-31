namespace RuinaoSoftwareWpf;

using RuinaoSoftwareWpf.ApplicationContracts;

public interface IAssessmentRunStore
{
    Task<AssessmentProgressSnapshot> GetProgressAsync(
        string patientCode,
        int totalModuleCount,
        CancellationToken cancellationToken = default);

    Task<AssessmentModuleRunContext> StartModuleAsync(
        AssessmentModuleStartRequest request,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken = default);

    Task MarkSavingAsync(
        long attemptId,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default);

    Task<AssessmentModuleResult> CompleteModuleAsync(
        long attemptId,
        string? resultJson,
        DateTimeOffset endedAt,
        CancellationToken cancellationToken = default);

    Task<AssessmentModuleResult> CancelModuleAsync(
        long attemptId,
        string reason,
        DateTimeOffset endedAt,
        CancellationToken cancellationToken = default);

    Task<AssessmentModuleResult> FailModuleAsync(
        long attemptId,
        string errorCode,
        string message,
        DateTimeOffset endedAt,
        CancellationToken cancellationToken = default);
}
