namespace RuinaoSoftwareWpf;

internal sealed record ReleaseIntegritySnapshot(
    bool IsValid,
    long VerifiedCount,
    string Message,
    DateTimeOffset CompletedAt,
    string? ManifestIdentity)
{
    public IntegrityCheckResult ToResult() => new(
        IntegrityCheckKind.ReleaseFiles,
        IsValid,
        VerifiedCount,
        Message,
        CompletedAt);
}
