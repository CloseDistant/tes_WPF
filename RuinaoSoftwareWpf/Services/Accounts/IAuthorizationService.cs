namespace RuinaoSoftwareWpf;

public interface IAuthorizationService
{
    CurrentUserInfo RequireSignedIn();

    bool HasPermission(AppPermission permission);

    CurrentUserInfo Demand(AppPermission permission);
}
