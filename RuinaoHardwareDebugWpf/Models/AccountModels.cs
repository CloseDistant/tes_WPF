namespace RuinaoHardwareDebugWpf;

public static class AccountRoles
{
    public const int Admin = 1;
    public const int Doctor = 2;
    public const int Technician = 3;

    public static string GetName(int roleId)
    {
        return roleId switch
        {
            Admin => "Admin",
            Doctor => "Doctor",
            Technician => "Technician",
            _ => "Unknown"
        };
    }
}

public sealed record CurrentUserInfo(
    long UserId,
    string LoginName,
    string DisplayName,
    int RoleId,
    bool MustChangePassword)
{
    public string RoleName => AccountRoles.GetName(RoleId);
}

public sealed record AccountLoginResult(
    bool Succeeded,
    CurrentUserInfo? User,
    string Message);

public sealed record CreateAccountRequest(
    string LoginName,
    string Password,
    string ConfirmPassword,
    string DisplayName,
    int RoleId);

public sealed record ChangePasswordRequest(
    long UserId,
    string NewPassword,
    string ConfirmPassword);

public sealed record ResetPasswordRequest(
    long UserId,
    string NewPassword,
    string ConfirmPassword);

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
