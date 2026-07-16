namespace RuinaoSoftwareWpf;

using System.Windows;
using RuinaoSoftwareWpf.Views;
using RuinaoSoftwareWpf.Views.Dialogs;

public partial class MainWindow
{
    private static readonly TimeSpan AuthenticationTimeout = TimeSpan.FromSeconds(30);

    private void RegisterAuthenticationEvents()
    {
        Loaded += OnAuthenticationLoaded;
        LoginContent.LoginRequested += OnLoginRequested;
        LoginContent.ExitRequested += OnCloseRequested;
        accountService.CurrentUserChanged += OnCurrentUserChanged;
    }

    private void UnregisterAuthenticationEvents()
    {
        Loaded -= OnAuthenticationLoaded;
        LoginContent.LoginRequested -= OnLoginRequested;
        LoginContent.ExitRequested -= OnCloseRequested;
        accountService.CurrentUserChanged -= OnCurrentUserChanged;
    }

    private async void OnAuthenticationLoaded(object sender, RoutedEventArgs e)
    {
        using var timeout = new CancellationTokenSource(AuthenticationTimeout);
        try
        {
            await accountService.InitializeAsync(timeout.Token);
            await ShowLoginContentAsync();
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            logger.Warning("登录页初始化超时");
            LoginContent.ShowMessage("初始化超时，请重试登录；如仍失败请查看运行日志。", true);
        }
        catch (Exception exception)
        {
            logger.Error("登录页初始化失败", exception);
            LoginContent.ShowMessage($"初始化失败：{exception.Message}", true);
        }
    }

    private async void OnLoginRequested(object? sender, LoginRequestedEventArgs e)
    {
        using var timeout = new CancellationTokenSource(AuthenticationTimeout);
        LoginContent.SetBusy(true);
        try
        {
            var result = await accountService.LoginAsync(e.LoginName, e.Password, timeout.Token);
            LoginContent.ClearPassword();
            if (!result.Succeeded || result.User is null)
            {
                LoginContent.ShowMessage(result.Message, true);
                return;
            }

            // Only the login name is persisted. Passwords never enter app_state or logs.
            await accountService.SetRememberedLoginNameAsync(
                e.RememberAccount ? result.User.LoginName : null);

            if (result.User.MustChangePassword)
            {
                var outcome = await ForceChangePasswordAsync(result.User);
                await ShowLoginContentAsync(outcome.Message, outcome.IsError);
                return;
            }

            ShowMainContent();
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            logger.Warning("登录操作超时");
            LoginContent.ShowMessage("登录超时，请稍后重试；如仍失败请查看运行日志。", true);
        }
        catch (Exception exception)
        {
            logger.Error("登录失败", exception);
            LoginContent.ShowMessage($"登录失败：{exception.Message}", true);
        }
        finally
        {
            LoginContent.SetBusy(false);
        }
    }

    private async Task<PasswordChangeOutcome> ForceChangePasswordAsync(CurrentUserInfo user)
    {
        string? error = null;
        while (true)
        {
            var dialog = new ChangePasswordDialog { Owner = this };
            if (!string.IsNullOrWhiteSpace(error))
            {
                dialog.ErrorMessage = error;
            }

            if (dialog.ShowDialog() != true)
            {
                await accountService.LogoutAsync();
                return new PasswordChangeOutcome("首次登录必须修改密码，请重新登录", true);
            }

            try
            {
                await accountService.ChangePasswordAsync(new ChangePasswordRequest(
                    user.UserId,
                    dialog.NewPassword,
                    dialog.ConfirmPassword));
                return new PasswordChangeOutcome("密码已修改，请使用新密码重新登录", false);
            }
            catch (Exception exception)
            {
                error = exception.Message;
            }
        }
    }

    private void OnCurrentUserChanged(object? sender, EventArgs e)
    {
        if (accountService.CurrentUser is not null
            || LoginContent.Visibility == Visibility.Visible)
        {
            return;
        }

        _ = Dispatcher.BeginInvoke(ReturnToLoginAfterLogout);
    }

    private async void ReturnToLoginAfterLogout()
    {
        try
        {
            await ShowLoginContentAsync();
        }
        catch (Exception exception)
        {
            logger.Error("返回登录页失败", exception);
            LoginContent.ShowMessage($"读取账号信息失败：{exception.Message}", true);
        }
    }

    private async Task ShowLoginContentAsync(string? message = null, bool isError = false)
    {
        MainContent.Visibility = Visibility.Collapsed;
        LoginContent.Visibility = Visibility.Visible;
        LoginContent.ClearPassword();

        var rememberedLoginName = await accountService.GetRememberedLoginNameAsync();
        LoginContent.ApplyRememberedLoginName(rememberedLoginName);
        LoginContent.ShowMessage(message ?? string.Empty, isError);
    }

    private void ShowMainContent()
    {
        LoginContent.Visibility = Visibility.Collapsed;
        MainContent.Visibility = Visibility.Visible;
        logger.Info($"进入主界面：userId={accountService.CurrentUser?.UserId}");
    }

    private sealed record PasswordChangeOutcome(string Message, bool IsError);
}
