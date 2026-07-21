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
