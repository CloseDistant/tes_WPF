namespace RuinaoSoftwareWpf;

public enum IntegrityCheckKind
{
    ReleaseFiles
}

public sealed record IntegrityCheckProgress(
    string Stage,
    string CurrentItem,
    long Completed,
    long Total)
{
    public int Percentage => Total <= 0
        ? 0
        : (int)Math.Clamp(Completed * 100 / Total, 0, 100);
}

public sealed record IntegrityCheckResult(
    IntegrityCheckKind Kind,
    bool IsValid,
    long VerifiedCount,
    string Message,
    DateTimeOffset CompletedAt);

public enum ReleaseIntegrityStatusKind
{
    NeverChecked,
    Passed,
    Failed,
    ReleaseChanged
}

public sealed record ReleaseIntegrityStatus(
    ReleaseIntegrityStatusKind Kind,
    IntegrityCheckResult? LastResult);

internal sealed record ReleaseIntegritySnapshot(
    bool IsValid,
    long VerifiedCount,
    string Message,
    DateTimeOffset CompletedAt,
    string? ManifestIdentity)
{
    public IntegrityCheckResult ToResult()
    {
        return new IntegrityCheckResult(
            IntegrityCheckKind.ReleaseFiles,
            IsValid,
            VerifiedCount,
            Message,
            CompletedAt);
    }
}
