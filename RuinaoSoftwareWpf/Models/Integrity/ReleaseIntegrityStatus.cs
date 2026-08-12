namespace RuinaoSoftwareWpf;

public sealed record ReleaseIntegrityStatus(ReleaseIntegrityStatusKind Kind, IntegrityCheckResult? LastResult);
