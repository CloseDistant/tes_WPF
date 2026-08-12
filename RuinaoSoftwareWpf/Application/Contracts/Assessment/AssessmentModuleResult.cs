namespace RuinaoSoftwareWpf.ApplicationContracts;

public sealed record AssessmentModuleResult(
    long RunId, long AttemptId, AssessmentModuleExecutionStatus Status,
    DateTimeOffset StartedAt, DateTimeOffset EndedAt, string? ErrorCode, string? Message);
