namespace RuinaoSoftwareWpf;

public enum AppPermission
{
    DeletePrescription,
    ManageStartupSettings,
    ManagePatients,
    ManageBackupRestore
}

public interface IAuthorizationService
{
    CurrentUserInfo RequireSignedIn();

    bool HasPermission(AppPermission permission);

    CurrentUserInfo Demand(AppPermission permission);
}
