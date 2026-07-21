namespace RuinaoSoftwareWpf;

public static class AuthorizationPolicy
{
    public static bool HasPermission(int roleId, AppPermission permission)
    {
        return permission switch
        {
            AppPermission.DeletePrescription or AppPermission.ManageStartupSettings or AppPermission.ManageBackupRestore =>
                roleId == AccountRoles.Admin,
            AppPermission.ManagePatients =>
                roleId is AccountRoles.Admin or AccountRoles.Doctor,
            _ => false
        };
    }
}
