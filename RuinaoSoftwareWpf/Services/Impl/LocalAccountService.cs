namespace RuinaoSoftwareWpf;

using Microsoft.EntityFrameworkCore;
using System.IO;

public sealed class LocalAccountService : IAccountService
{
    private const string DefaultAdminLoginName = "Admin";
    private const string DefaultAdminPassword = "123456";
    private const string RememberedLoginNameKey = "last_login_name";
    private const int MaxFailedLoginAttempts = 5;
    private static readonly TimeSpan LoginLockoutDuration = TimeSpan.FromMinutes(30);

    private readonly ILoggingService logger;
    private readonly IAppDatabaseInitializer databaseInitializer;
    private readonly IAppDatabaseWriteCoordinator databaseWriteCoordinator;
    private readonly IAuditTrailService auditTrail;
    private readonly SemaphoreSlim databaseGate = new(1, 1);
    private readonly SemaphoreSlim loginGate = new(1, 1);
    private bool initialized;
    private CurrentUserInfo? currentUser;

    public LocalAccountService(
        ILoggingService logger,
        IAppDatabaseInitializer databaseInitializer,
        IAppDatabaseWriteCoordinator databaseWriteCoordinator,
        IAuditTrailService auditTrail)
    {
        this.logger = logger;
        this.databaseInitializer = databaseInitializer;
        this.databaseWriteCoordinator = databaseWriteCoordinator;
        this.auditTrail = auditTrail;
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

    public async Task<string?> GetRememberedLoginNameAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        await using var context = new CaptureDbContext(GetDatabasePath());
        return await context.AppStates
            .AsNoTracking()
            .Where(item => item.Key == RememberedLoginNameKey)
            .Select(item => item.Value)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task SetRememberedLoginNameAsync(string? loginName, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        await using var context = new CaptureDbContext(GetDatabasePath());
        var state = await context.AppStates.FirstOrDefaultAsync(
            item => item.Key == RememberedLoginNameKey,
            cancellationToken);
        var normalizedLoginName = string.IsNullOrWhiteSpace(loginName) ? null : loginName.Trim();

        if (normalizedLoginName is null)
        {
            if (state is not null)
            {
                context.AppStates.Remove(state);
                await SaveChangesAsync(context, cancellationToken);
            }

            return;
        }

        var now = DateTimeOffset.Now.ToUnixTimeMilliseconds();
        if (state is null)
        {
            context.AppStates.Add(new AppStateEntity
            {
                Key = RememberedLoginNameKey,
                Value = normalizedLoginName,
                UpdatedAtUnixMs = now
            });
        }
        else
        {
            state.Value = normalizedLoginName;
            state.UpdatedAtUnixMs = now;
        }

        await SaveChangesAsync(context, cancellationToken);
    }

    public async Task<AccountLoginResult> LoginAsync(string loginName, string password, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(loginName) || string.IsNullOrWhiteSpace(password))
        {
            await WriteAuditAsync(null, null, "login", "failed", "登录名和密码不能为空", cancellationToken);
            return new AccountLoginResult(false, null, "登录名和密码不能为空");
        }

        var normalizedLoginName = loginName.Trim();
        if (!IsValidLoginName(normalizedLoginName))
        {
            await WriteAuditAsync(null, null, "login", "failed", "登录名格式不正确", cancellationToken);
            return new AccountLoginResult(false, null, "登录名或密码错误");
        }

        await loginGate.WaitAsync(cancellationToken);
        try
        {
            await using var context = new AccountDbContext(GetDatabasePath());
            var user = await context.Users.FirstOrDefaultAsync(
                item => item.LoginName == normalizedLoginName && item.IsActive,
                cancellationToken);

            if (user is null)
            {
                await WriteAuditAsync(null, null, "login", "failed", "登录名或密码错误", cancellationToken);
                return new AccountLoginResult(false, null, "登录名或密码错误");
            }

            var now = DateTimeOffset.Now;
            if (user.LockoutEndUnixMs is long lockoutEndUnixMs
                && lockoutEndUnixMs > now.ToUnixTimeMilliseconds())
            {
                var message = FormatLockoutMessage(lockoutEndUnixMs, now);
                await WriteAuditAsync(null, user.Id, "login", "blocked", message, cancellationToken);
                return new AccountLoginResult(false, null, message);
            }

            if (user.LockoutEndUnixMs is not null)
            {
                user.FailedLoginAttempts = 0;
                user.LockoutEndUnixMs = null;
            }

            if (!PasswordHasher.VerifyPassword(password, user.PasswordHash, user.PasswordSalt))
            {
                user.FailedLoginAttempts++;
                var message = BuildFailedLoginMessage(user, now);
                user.UpdatedAtUnixMs = now.ToUnixTimeMilliseconds();
                await SaveChangesAsync(context, cancellationToken);
                await WriteAuditAsync(null, user.Id, "login", "failed", message, cancellationToken);
                return new AccountLoginResult(false, null, message);
            }

            user.FailedLoginAttempts = 0;
            user.LockoutEndUnixMs = null;
            user.UpdatedAtUnixMs = now.ToUnixTimeMilliseconds();
            await SaveChangesAsync(context, cancellationToken);

            var info = ToCurrentUser(user);
            CurrentUser = info;
            await WriteAuditAsync(info.UserId, info.UserId, "login", "success", "登录成功", cancellationToken);
            logger.Info($"账号登录：userId={info.UserId}, role={info.RoleName}");
            return new AccountLoginResult(true, info, user.MustChangePassword ? "首次登录需要修改密码" : "登录成功");
        }
        finally
        {
            loginGate.Release();
        }
    }

    public async Task<AccountPasswordVerificationResult> VerifyCurrentPasswordAsync(
        string password,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        var currentUser = CurrentUser;
        if (currentUser is null)
        {
            return new AccountPasswordVerificationResult(false, false, "当前没有已登录账号");
        }

        await loginGate.WaitAsync(cancellationToken);
        try
        {
            await using var context = new AccountDbContext(GetDatabasePath());
            var user = await context.Users.FirstOrDefaultAsync(
                item => item.Id == currentUser.UserId && item.IsActive,
                cancellationToken);
            if (user is null)
            {
                await WriteAuditAsync(
                    currentUser.UserId,
                    currentUser.UserId,
                    "session_unlock",
                    "failed",
                    "当前账号不存在或已停用",
                    cancellationToken);
                return new AccountPasswordVerificationResult(false, false, "当前账号不存在或已停用");
            }

            var now = DateTimeOffset.Now;
            if (user.LockoutEndUnixMs is long lockoutEndUnixMs
                && lockoutEndUnixMs > now.ToUnixTimeMilliseconds())
            {
                var message = FormatLockoutMessage(lockoutEndUnixMs, now);
                await WriteAuditAsync(user.Id, user.Id, "session_unlock", "blocked", message, cancellationToken);
                return new AccountPasswordVerificationResult(false, true, message);
            }

            if (user.LockoutEndUnixMs is not null)
            {
                user.FailedLoginAttempts = 0;
                user.LockoutEndUnixMs = null;
            }

            if (string.IsNullOrEmpty(password)
                || !PasswordHasher.VerifyPassword(password, user.PasswordHash, user.PasswordSalt))
            {
                user.FailedLoginAttempts++;
                var message = BuildFailedPasswordVerificationMessage(user, now);
                user.UpdatedAtUnixMs = now.ToUnixTimeMilliseconds();
                await SaveChangesAsync(context, cancellationToken);
                await WriteAuditAsync(user.Id, user.Id, "session_unlock", "failed", message, cancellationToken);
                return new AccountPasswordVerificationResult(false, user.LockoutEndUnixMs is not null, message);
            }

            user.FailedLoginAttempts = 0;
            user.LockoutEndUnixMs = null;
            user.UpdatedAtUnixMs = now.ToUnixTimeMilliseconds();
            await SaveChangesAsync(context, cancellationToken);
            await WriteAuditAsync(user.Id, user.Id, "session_unlock", "success", "会话解锁成功", cancellationToken);
            return new AccountPasswordVerificationResult(true, false, "解锁成功");
        }
        finally
        {
            loginGate.Release();
        }
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
        await SaveChangesAsync(context, cancellationToken);
        await WriteAuditAsync(operatorUser.UserId, user.Id, "create_user", "success", $"创建账号：{user.LoginName}", cancellationToken);
        logger.Info($"创建账号：operator={operatorUser.UserId}, target={user.Id}, role={AccountRoles.GetName(user.RoleId)}");
        return ToCurrentUser(user);
    }

    public async Task<PageResult<AccountListItemInfo>> GetAccountListPageAsync(
        PageRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await EnsureInitializedAsync(cancellationToken);

        var operatorUser = CurrentUser;
        if (operatorUser is null || operatorUser.RoleId != AccountRoles.Admin)
        {
            await WriteAuditAsync(
                operatorUser?.UserId,
                null,
                "view_account_list",
                "failed",
                "非 Admin 账号尝试查看账号列表",
                cancellationToken);
            throw new InvalidOperationException("只有 Admin 可以查看账号列表");
        }

        await using var context = new AccountDbContext(GetDatabasePath());
        var users = await context.Users
            .AsNoTracking()
            .OrderBy(item => item.RoleId)
            .ThenBy(item => item.Id)
            .Select(item => new AccountListItemInfo(
                item.Id,
                item.LoginName,
                item.DisplayName,
                item.RoleId,
                item.IsActive,
                item.CreatedAtUnixMs))
            .Skip(request.SafeOffset)
            .Take(request.SafePageSize + 1)
            .ToListAsync(cancellationToken);
        var hasMore = users.Count > request.SafePageSize;
        if (hasMore)
        {
            users.RemoveAt(users.Count - 1);
        }

        await WriteAuditAsync(
            operatorUser.UserId,
            null,
            "view_account_list",
            "success",
            $"查看账号列表，共 {users.Count} 个账号",
            cancellationToken);

        return new PageResult<AccountListItemInfo>(users, hasMore);
    }

    public async Task<IReadOnlyList<string>> GetActiveLoginNamesAsync(
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        if (!IsCurrentUserAdmin())
        {
            throw new UnauthorizedAccessException("只有Admin可以获取账号筛选列表");
        }

        await using var context = new AccountDbContext(GetDatabasePath());
        return await context.Users
            .AsNoTracking()
            .Where(item => item.IsActive)
            .OrderBy(item => item.LoginName)
            .Select(item => item.LoginName)
            .ToListAsync(cancellationToken);
    }

    public async Task ChangePasswordAsync(ChangePasswordRequest request, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        if (CurrentUser?.UserId != request.UserId)
        {
            await WriteAuditAsync(
                CurrentUser?.UserId,
                request.UserId,
                "change_password",
                "failed",
                "尝试通过个人改密接口修改其他账号",
                cancellationToken);
            throw new InvalidOperationException("只能修改当前登录账号的密码");
        }

        try
        {
            AccountPasswordPolicy.Validate(request.NewPassword, request.ConfirmPassword);
        }
        catch (InvalidOperationException ex)
        {
            await WriteAuditAsync(
                CurrentUser?.UserId,
                request.UserId,
                "change_password",
                "failed",
                ex.Message,
                cancellationToken);
            throw;
        }

        await using var context = new AccountDbContext(GetDatabasePath());
        var user = await context.Users.FirstOrDefaultAsync(item => item.Id == request.UserId && item.IsActive, cancellationToken)
            ?? throw new InvalidOperationException("账号不存在或已停用");

        var password = PasswordHasher.HashPassword(request.NewPassword);
        user.PasswordHash = password.Hash;
        user.PasswordSalt = password.Salt;
        user.FailedLoginAttempts = 0;
        user.LockoutEndUnixMs = null;
        user.MustChangePassword = false;
        user.UpdatedAtUnixMs = DateTimeOffset.Now.ToUnixTimeMilliseconds();
        context.Users.Update(user);
        await SaveChangesAsync(context, cancellationToken);

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

    public async Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        var operatorUser = CurrentUser;
        if (operatorUser is null || operatorUser.RoleId != AccountRoles.Admin)
        {
            await WriteAuditAsync(
                operatorUser?.UserId,
                request.UserId,
                "reset_password",
                "failed",
                "非 Admin 账号尝试重置密码",
                cancellationToken);
            throw new InvalidOperationException("只有 Admin 可以重置账号密码");
        }

        try
        {
            AccountPasswordPolicy.Validate(request.NewPassword, request.ConfirmPassword);
        }
        catch (InvalidOperationException ex)
        {
            await WriteAuditAsync(
                operatorUser.UserId,
                request.UserId,
                "reset_password",
                "failed",
                ex.Message,
                cancellationToken);
            throw;
        }

        await using var context = new AccountDbContext(GetDatabasePath());
        var user = await context.Users.FirstOrDefaultAsync(item => item.Id == request.UserId && item.IsActive, cancellationToken)
            ?? throw new InvalidOperationException("账号不存在或已停用");

        var password = PasswordHasher.HashPassword(request.NewPassword);
        user.PasswordHash = password.Hash;
        user.PasswordSalt = password.Salt;
        user.FailedLoginAttempts = 0;
        user.LockoutEndUnixMs = null;
        user.MustChangePassword = true;
        user.UpdatedAtUnixMs = DateTimeOffset.Now.ToUnixTimeMilliseconds();
        await SaveChangesAsync(context, cancellationToken);

        await WriteAuditAsync(
            operatorUser.UserId,
            user.Id,
            "reset_password",
            "success",
            $"重置账号密码：{user.LoginName}",
            cancellationToken);
        logger.Info($"重置账号密码：operator={operatorUser.UserId}, target={user.Id}");

        if (operatorUser.UserId == user.Id)
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

    private async Task EnsureDefaultAdminAsync(AccountDbContext context, CancellationToken cancellationToken)
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
        await SaveChangesAsync(context, cancellationToken);
    }

    private Task<int> SaveChangesAsync(DbContext context, CancellationToken cancellationToken)
    {
        return databaseWriteCoordinator.ExecuteAsync(
            GetDatabasePath(),
            () => context.SaveChangesAsync(cancellationToken),
            cancellationToken);
    }

    private async Task WriteAuditAsync(long? operatorUserId, long? targetUserId, string action, string result, string? message, CancellationToken cancellationToken)
    {
        var current = CurrentUser;
        var actor = current is not null && current.UserId == operatorUserId
            ? AuditActor.From(current)
            : new AuditActor(operatorUserId, operatorUserId is null ? "system" : $"user-{operatorUserId}", null);
        var mapped = AuditActionCatalog.FromLegacyAction(action);
        var auditResult = AuditActionCatalog.ParseResult(result);
        await auditTrail.AppendAsync(
            new AuditEventInput(
                mapped.Category,
                mapped.ActionCode,
                actor,
                targetUserId is null ? "None" : "User",
                targetUserId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
                auditResult,
                auditResult == AuditEventResult.Success ? null : mapped.ActionCode,
                message),
            cancellationToken);
    }

    private static string BuildFailedLoginMessage(AccountUserEntity user, DateTimeOffset now)
    {
        if (user.FailedLoginAttempts >= MaxFailedLoginAttempts)
        {
            user.FailedLoginAttempts = MaxFailedLoginAttempts;
            user.LockoutEndUnixMs = now.Add(LoginLockoutDuration).ToUnixTimeMilliseconds();
            return "密码连续输错 5 次，账号已限制登录 30 分钟";
        }

        var remainingAttempts = MaxFailedLoginAttempts - user.FailedLoginAttempts;
        return $"登录名或密码错误，仅剩 {remainingAttempts} 次机会";
    }

    private static string BuildFailedPasswordVerificationMessage(AccountUserEntity user, DateTimeOffset now)
    {
        if (user.FailedLoginAttempts >= MaxFailedLoginAttempts)
        {
            user.FailedLoginAttempts = MaxFailedLoginAttempts;
            user.LockoutEndUnixMs = now.Add(LoginLockoutDuration).ToUnixTimeMilliseconds();
            return "密码连续输错 5 次，账号已限制验证 30 分钟";
        }

        var remainingAttempts = MaxFailedLoginAttempts - user.FailedLoginAttempts;
        return $"密码错误，仅剩 {remainingAttempts} 次机会";
    }

    private static string FormatLockoutMessage(long lockoutEndUnixMs, DateTimeOffset now)
    {
        var remaining = DateTimeOffset.FromUnixTimeMilliseconds(lockoutEndUnixMs) - now;
        var remainingMinutes = Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes));
        return $"该账号已限制登录，请在 {remainingMinutes} 分钟后重试";
    }

    private static void ValidateCreateUserRequest(CreateAccountRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.LoginName)
            || string.IsNullOrWhiteSpace(request.DisplayName))
        {
            throw new InvalidOperationException("登录名和姓名不能为空");
        }

        if (!IsValidLoginName(request.LoginName.Trim()))
        {
            throw new InvalidOperationException("账号只能使用英文字母和数字");
        }

        AccountPasswordPolicy.Validate(request.Password, request.ConfirmPassword);

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
