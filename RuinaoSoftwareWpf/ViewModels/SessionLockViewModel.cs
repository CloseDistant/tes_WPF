namespace RuinaoSoftwareWpf;

using System.Globalization;
using System.Windows;
using System.Windows.Threading;

public sealed class SessionLockViewModel : ObservableObject
{
    private readonly ISessionSecurityService sessionSecurityService;
    private readonly IAccountService accountService;
    private readonly ILoggingService logger;
    private readonly DispatcherTimer clockTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private string currentTimeText = string.Empty;
    private string currentDateText = string.Empty;
    private string message = string.Empty;
    private bool isError;
    private bool isBusy;

    public SessionLockViewModel(
        ISessionSecurityService sessionSecurityService,
        IAccountService accountService,
        ILoggingService logger)
    {
        this.sessionSecurityService = sessionSecurityService;
        this.accountService = accountService;
        this.logger = logger;

        clockTimer.Tick += (_, _) => UpdateClock();
        sessionSecurityService.StateChanged += (_, _) => RefreshStateOnUiThread();
        accountService.CurrentUserChanged += (_, _) => RefreshStateOnUiThread();
        UpdateClock();
    }

    public Visibility Visibility => sessionSecurityService.IsLocked
        ? Visibility.Visible
        : Visibility.Collapsed;

    public string CurrentAccount => accountService.CurrentUser?.LoginName ?? string.Empty;

    public string CurrentRole => accountService.CurrentUser?.RoleId switch
    {
        AccountRoles.Admin => "管理员",
        AccountRoles.Doctor => "医师",
        AccountRoles.Technician => "技术员",
        _ => string.Empty
    };

    public string AccountInitial
    {
        get
        {
            var account = CurrentAccount.Trim();
            return account.Length == 0 ? "?" : account[..1].ToUpperInvariant();
        }
    }

    public string CurrentTimeText
    {
        get => currentTimeText;
        private set => SetProperty(ref currentTimeText, value);
    }

    public string CurrentDateText
    {
        get => currentDateText;
        private set => SetProperty(ref currentDateText, value);
    }

    public string LockedAtText => sessionSecurityService.LockedAtUtc is { } lockedAt
        ? $"锁定时间 {lockedAt.ToLocalTime():HH:mm}"
        : string.Empty;

    public string Message
    {
        get => message;
        private set => SetProperty(ref message, value);
    }

    public bool IsError
    {
        get => isError;
        private set => SetProperty(ref isError, value);
    }

    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (SetProperty(ref isBusy, value))
            {
                OnPropertyChanged(nameof(CanUnlock));
            }
        }
    }

    public bool CanUnlock => !IsBusy;

    public async Task<SessionUnlockResult> UnlockAsync(
        string password,
        CancellationToken cancellationToken = default)
    {
        if (IsBusy)
        {
            return new SessionUnlockResult(false, false, "正在验证密码");
        }

        IsBusy = true;
        Message = string.Empty;
        IsError = false;
        try
        {
            var result = await sessionSecurityService.UnlockAsync(password, cancellationToken);
            Message = result.Succeeded ? string.Empty : result.Message;
            IsError = !result.Succeeded;
            return result;
        }
        catch (Exception exception)
        {
            logger.Error("会话解锁操作失败", exception);
            Message = "解锁失败，请稍后重试";
            IsError = true;
            return new SessionUnlockResult(false, false, Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void ClearMessage()
    {
        Message = string.Empty;
        IsError = false;
    }

    private void RefreshStateOnUiThread()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            _ = dispatcher.BeginInvoke(RefreshState);
            return;
        }

        RefreshState();
    }

    private void RefreshState()
    {
        OnPropertyChanged(nameof(Visibility));
        OnPropertyChanged(nameof(CurrentAccount));
        OnPropertyChanged(nameof(CurrentRole));
        OnPropertyChanged(nameof(AccountInitial));
        OnPropertyChanged(nameof(LockedAtText));

        if (sessionSecurityService.IsLocked)
        {
            UpdateClock();
            clockTimer.Start();
        }
        else
        {
            clockTimer.Stop();
            ClearMessage();
        }
    }

    private void UpdateClock()
    {
        var now = DateTime.Now;
        CurrentTimeText = now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        CurrentDateText = now.ToString("yyyy年M月d日  dddd", CultureInfo.GetCultureInfo("zh-CN"));
    }
}
