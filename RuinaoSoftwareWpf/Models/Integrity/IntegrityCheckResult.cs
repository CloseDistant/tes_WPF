namespace RuinaoSoftwareWpf;

public sealed record IntegrityCheckResult(
    IntegrityCheckKind Kind,
    bool IsValid,
    long VerifiedCount,
    string Message,
    DateTimeOffset CompletedAt);
