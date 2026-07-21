namespace RuinaoSoftwareWpf;

public enum AuditEventCategory
{
    IdentitySession = 1,
    AccountAuthorization = 2,
    PatientManagement = 3,
    PrescriptionManagement = 4,
    StimulationDevice = 5,
    SecurityConfiguration = 6,
    DataExport = 7,
    AuditSystem = 8,
    IntegrityCheck = 9
}

public enum AuditEventResult
{
    Success = 1,
    Failed = 2,
    Blocked = 3
}

public sealed record AuditActor(
    long? UserId,
    string LoginName,
    int? RoleId)
{
    public static AuditActor System { get; } = new(null, "system", null);

    public static AuditActor From(CurrentUserInfo? user)
    {
        return user is null
            ? System
            : new AuditActor(user.UserId, user.LoginName, user.RoleId);
    }
}

public sealed record AuditEventInput(
    AuditEventCategory Category,
    string ActionCode,
    AuditActor Actor,
    string TargetType,
    string TargetId,
    AuditEventResult Result,
    string? FailureCode = null,
    string? Reason = null,
    DateTimeOffset? OccurredAtUtc = null);

public sealed record AuditQuery(
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    AuditEventCategory? Category,
    string? ActorLoginName,
    int PageNumber = 1,
    int PageSize = 50);

public sealed record AuditEventRecord(
    long SequenceNo,
    string EventId,
    DateTimeOffset OccurredAtUtc,
    long? ActorUserId,
    string ActorLoginName,
    int? ActorRoleId,
    string SessionId,
    AuditEventCategory Category,
    string ActionCode,
    string TargetType,
    string TargetId,
    AuditEventResult Result,
    string? FailureCode,
    string? Reason,
    string WorkstationId,
    string SoftwareVersion);

public sealed record AuditQueryResult(
    IReadOnlyList<AuditEventRecord> Items,
    long TotalCount,
    int PageNumber,
    int PageSize);

public sealed record AuditIntegrityResult(
    bool IsValid,
    long VerifiedCount,
    long? BrokenSequenceNo,
    string Message,
    DateTimeOffset VerifiedAtUtc);

public sealed record AuditExportResult(
    string FilePath,
    long ExportedCount,
    string Sha256,
    DateTimeOffset ExportedAtUtc);

public sealed record AuditCategoryOption(
    AuditEventCategory? Value,
    string DisplayName)
{
    public override string ToString() => DisplayName;
}

public sealed record AuditActorOption(
    string? LoginName,
    string DisplayName)
{
    public override string ToString() => DisplayName;
}
