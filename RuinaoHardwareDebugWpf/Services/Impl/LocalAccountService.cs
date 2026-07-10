namespace RuinaoHardwareDebugWpf;

using Microsoft.EntityFrameworkCore;
using System.IO;

public sealed class LocalAccountService : IAccountService
{
    private const string DefaultAdminLoginName = "Admin";
    private const string DefaultAdminPassword = "123456";

    private readonly ILoggingService logger;
    private readonly IAppDatabaseInitializer databaseInitializer;
    private readonly SemaphoreSlim databaseGate = new(1, 1);
    private bool initialized;
    private CurrentUserInfo? currentUser;

    public LocalAccountService(ILoggingService logger, IAppDatabaseInitializer databaseInitializer)
    {
        this.logger = logger;
        this.databaseInitializer = databaseInitializer;
    }

    public event EventHandler? CurrentUserChanged;

    public CurrentUserInfo? CurrentUser
    {
        get => currentUser;
        private set
        {
            currentUser = value;
            CurrentUserChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
    }

    public async Task<AccountLoginResult> LoginAsync(string loginName, string password, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(loginName) || string.IsNullOrWhiteSpace(password))
        {
            await WriteAuditAsync(null, null, "login", "failed", "登录名和密码不能为空", cancellationToken);
            return new AccountLoginResult(false, null, "登录名和密码不能为空");
        }

        await using var context = new AccountDbContext(GetDatabasePath());
        var normalizedLoginName = loginName.Trim();
        if (!IsValidLoginName(normalizedLoginName))
        {
            await WriteAuditAsync(null, null, "login", "failed", "登录名格式不正确", cancellationToken);
            return new AccountLoginResult(false, null, "登录名或密码错误");
        }

        var user = await context.Users.FirstOrDefaultAsync(
            item => item.LoginName == normalizedLoginName && item.IsActive,
            cancellationToken);

        if (user is null || !PasswordHasher.VerifyPassword(password, user.PasswordHash, user.PasswordSalt))
        {
            await WriteAuditAsync(null, user?.Id, "login", "failed", "登录名或密码错误", cancellationToken);
            return new AccountLoginResult(false, null, "登录名或密码错误");
        }

        var info = ToCurrentUser(user);
        CurrentUser = info;
        await WriteAuditAsync(info.UserId, info.UserId, "login", "success", "登录成功", cancellationToken);
        logger.Info($"账号登录：userId={info.UserId}, role={info.RoleName}");
        return new AccountLoginResult(true, info, user.MustChangePassword ? "首次登录需要修改密码" : "登录成功");
    }

    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        var previousUser = CurrentUser;
        if (previousUser is not null)
        {
            await WriteAuditAsync(previousUser.UserId, previousUser.UserId, "logout", "success", "退出登录", cancellationToken);
            logger.Info($"账号退出：userId={previousUser.UserId}");
        }

        CurrentUser = null;
    }

    public async Task<CurrentUserInfo> CreateUserAsync(CreateAccountRequest request, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        var operatorUser = CurrentUser;
        if (operatorUser is null || operatorUser.RoleId != AccountRoles.Admin)
        {
            await WriteAuditAsync(operatorUser?.UserId, null, "create_user", "failed", "非 Admin 账号尝试创建用户", cancellationToken);
            throw new InvalidOperationException("只有 Admin 可以注册账号");
        }

        ValidateCreateUserRequest(request);

        await using var context = new AccountDbContext(GetDatabasePath());
        var normalizedLoginName = request.LoginName.Trim();
        var exists = await context.Users.AnyAsync(item => item.LoginName == normalizedLoginName, cancellationToken);
        if (exists)
        {
            await WriteAuditAsync(operatorUser.UserId, null, "create_user", "failed", $"登录名已存在：{normalizedLoginName}", cancellationToken);
            throw new InvalidOperationException("登录名已存在");
        }

        var now = DateTimeOffset.Now.ToUnixTimeMilliseconds();
        var password = PasswordHasher.HashPassword(request.Password);
        var user = new AccountUserEntity
        {
            LoginName = normalizedLoginName,
            DisplayName = request.DisplayName.Trim(),
            RoleId = request.RoleId,
            PasswordHash = password.Hash,
            PasswordSalt = password.Salt,
            MustChangePassword = false,
            IsActive = true,
            CreatedAtUnixMs = now,
            UpdatedAtUnixMs = now
        };

        context.Users.Add(user);
        await context.SaveChangesAsync(cancellationToken);
        await WriteAuditAsync(operatorUser.UserId, user.Id, "create_user", "success", $"创建账号：{user.LoginName}", cancellationToken);
        logger.Info($"创建账号：operator={operatorUser.UserId}, target={user.Id}, role={AccountRoles.GetName(user.RoleId)}");
        return ToCurrentUser(user);
    }

    public async Task ChangePasswordAsync(ChangePasswordRequest request, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(request.NewPassword))
        {
            throw new InvalidOperationException("新密码不能为空");
        }

        if (request.NewPassword != request.ConfirmPassword)
        {
            throw new InvalidOperationException("两次输入的密码不一致");
        }

        await using var context = new AccountDbContext(GetDatabasePath());
        var user = await context.Users.FirstOrDefaultAsync(item => item.Id == request.UserId && item.IsActive, cancellationToken)
            ?? throw new InvalidOperationException("账号不存在或已停用");

        var password = PasswordHasher.HashPassword(request.NewPassword);
        user.PasswordHash = password.Hash;
        user.PasswordSalt = password.Salt;
        user.MustChangePassword = false;
        user.UpdatedAtUnixMs = DateTimeOffset.Now.ToUnixTimeMilliseconds();
        context.Users.Update(user);
        await context.SaveChangesAsync(cancellationToken);

        await WriteAuditAsync(
            CurrentUser?.UserId,
            user.Id,
            user.LoginName == DefaultAdminLoginName ? "force_change_password" : "change_password",
            "success",
            "修改密码",
            cancellationToken);
        logger.Info($"修改账号密码：target={user.Id}");

        if (CurrentUser?.UserId == request.UserId)
        {
            CurrentUser = null;
        }
    }

    public bool IsCurrentUserAdmin()
    {
        return CurrentUser?.RoleId == AccountRoles.Admin;
    }

    public async Task RecordAuditAsync(
        long? operatorUserId,
        long? targetUserId,
        string action,
        string result,
        string? message = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        await WriteAuditAsync(operatorUserId, targetUserId, action, result, message, cancellationToken);
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (initialized)
        {
            return;
        }

        await databaseGate.WaitAsync(cancellationToken);
        try
        {
            if (initialized)
            {
                return;
            }

            await databaseInitializer.EnsureInitializedAsync(cancellationToken);
            await using var context = new AccountDbContext(GetDatabasePath());
            await EnsureDefaultAdminAsync(context, cancellationToken);
            initialized = true;
        }
        finally
        {
            databaseGate.Release();
        }
    }

    private static async Task EnsureDefaultAdminAsync(AccountDbContext context, CancellationToken cancellationToken)
    {
        var hasAdmin = await context.Users.AnyAsync(item => item.RoleId == AccountRoles.Admin, cancellationToken);
        if (hasAdmin)
        {
            return;
        }

        var now = DateTimeOffset.Now.ToUnixTimeMilliseconds();
        var password = PasswordHasher.HashPassword(DefaultAdminPassword);
        context.Users.Add(new AccountUserEntity
        {
            LoginName = DefaultAdminLoginName,
            DisplayName = "Admin",
            RoleId = AccountRoles.Admin,
            PasswordHash = password.Hash,
            PasswordSalt = password.Salt,
            MustChangePassword = true,
            IsActive = true,
            CreatedAtUnixMs = now,
            UpdatedAtUnixMs = now
        });
        await context.SaveChangesAsync(cancellationToken);
    }

    private async Task WriteAuditAsync(long? operatorUserId, long? targetUserId, string action, string result, string? message, CancellationToken cancellationToken)
    {
        await using var context = new AccountDbContext(GetDatabasePath());
        context.AccountAuditLogs.Add(new AccountAuditLogEntity
        {
            OperatorUserId = operatorUserId,
            TargetUserId = targetUserId,
            Action = action,
            Result = result,
            Message = message,
            CreatedAtUnixMs = DateTimeOffset.Now.ToUnixTimeMilliseconds()
        });
        await context.SaveChangesAsync(cancellationToken);
    }

    private static void ValidateCreateUserRequest(CreateAccountRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.LoginName)
            || string.IsNullOrWhiteSpace(request.Password)
            || string.IsNullOrWhiteSpace(request.DisplayName))
        {
            throw new InvalidOperationException("登录名、密码和姓名不能为空");
        }

        if (!IsValidLoginName(request.LoginName.Trim()))
        {
            throw new InvalidOperationException("账号只能使用英文字母和数字");
        }

        if (request.Password != request.ConfirmPassword)
        {
            throw new InvalidOperationException("两次输入的密码不一致");
        }

        if (request.RoleId is not (AccountRoles.Doctor or AccountRoles.Technician))
        {
            throw new InvalidOperationException("职业只能选择 Doctor 或 Technician");
        }
    }

    private static bool IsValidLoginName(string loginName)
    {
        return loginName.Length > 0 && loginName.All(static ch => ch is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9');
    }

    private static CurrentUserInfo ToCurrentUser(AccountUserEntity user)
    {
        return new CurrentUserInfo(user.Id, user.LoginName, user.DisplayName, user.RoleId, user.MustChangePassword);
    }

    private static string GetDatabasePath()
    {
        return AppDatabasePathProvider.MainDatabasePath;
    }
}
