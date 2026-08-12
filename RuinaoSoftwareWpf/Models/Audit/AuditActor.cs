namespace RuinaoSoftwareWpf;

public sealed record AuditActor(long? UserId, string LoginName, int? RoleId)
{
    public static AuditActor System { get; } = new(null, "system", null);
    public static AuditActor From(CurrentUserInfo? user) => user is null
        ? System
        : new AuditActor(user.UserId, user.LoginName, user.RoleId);
}
