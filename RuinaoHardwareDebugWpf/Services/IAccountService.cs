namespace RuinaoHardwareDebugWpf;

public interface IAccountService
{
    CurrentUserInfo? CurrentUser { get; }

    event EventHandler? CurrentUserChanged;

    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task<string?> GetRememberedLoginNameAsync(CancellationToken cancellationToken = default);

    Task SetRememberedLoginNameAsync(string? loginName, CancellationToken cancellationToken = default);

    Task<AccountLoginResult> LoginAsync(string loginName, string password, CancellationToken cancellationToken = default);

    Task LogoutAsync(CancellationToken cancellationToken = default);

    Task<CurrentUserInfo> CreateUserAsync(CreateAccountRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AccountListItemInfo>> GetAccountListAsync(CancellationToken cancellationToken = default);

    Task ChangePasswordAsync(ChangePasswordRequest request, CancellationToken cancellationToken = default);

    Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default);

    Task RecordAuditAsync(long? operatorUserId, long? targetUserId, string action, string result, string? message = null, CancellationToken cancellationToken = default);

    bool IsCurrentUserAdmin();
}
