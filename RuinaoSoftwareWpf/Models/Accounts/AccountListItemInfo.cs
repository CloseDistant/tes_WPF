namespace RuinaoSoftwareWpf;

public sealed record AccountListItemInfo(
    long UserId,
    string LoginName,
    string DisplayName,
    int RoleId,
    bool IsActive,
    long CreatedAtUnixMs)
{
    public string RoleName => AccountRoles.GetName(RoleId);
}
