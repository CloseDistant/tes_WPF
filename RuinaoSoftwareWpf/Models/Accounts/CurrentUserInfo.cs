namespace RuinaoSoftwareWpf;

public sealed record CurrentUserInfo(
    long UserId,
    string LoginName,
    string DisplayName,
    int RoleId,
    bool MustChangePassword)
{
    public string RoleName => AccountRoles.GetName(RoleId);
}
