namespace RuinaoSoftwareWpf;

public sealed record AuditIntegrityResult(
    bool IsValid,
    long VerifiedCount,
    long? BrokenSequenceNo,
    string Message,
    DateTimeOffset VerifiedAtUtc);
