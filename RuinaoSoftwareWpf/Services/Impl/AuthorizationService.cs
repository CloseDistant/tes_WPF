namespace RuinaoSoftwareWpf;

public sealed class AuthorizationService : IAuthorizationService
{
    private readonly IAccountService accountService;

    public AuthorizationService(IAccountService accountService)
    {
        this.accountService = accountService;
    }

    public CurrentUserInfo RequireSignedIn()
    {
        return accountService.CurrentUser
            ?? throw new UnauthorizedAccessException("请先登录后再执行此操作");
    }

    public bool HasPermission(AppPermission permission)
    {
        var currentUser = accountService.CurrentUser;
        return currentUser is not null
            && AuthorizationPolicy.HasPermission(currentUser.RoleId, permission);
    }

    public CurrentUserInfo Demand(AppPermission permission)
    {
        var currentUser = RequireSignedIn();
        if (!AuthorizationPolicy.HasPermission(currentUser.RoleId, permission))
        {
            throw new UnauthorizedAccessException("当前账号无权执行此操作");
        }

        return currentUser;
    }
}
