namespace RuinaoSoftwareWpf;

public sealed record SafetyEvaluationResult(SafetyAction Action, string Reason, DateTimeOffset Timestamp);
