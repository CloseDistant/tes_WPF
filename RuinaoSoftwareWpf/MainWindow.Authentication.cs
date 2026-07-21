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
        ActivationContent.ActivationRequested += OnActivationRequested;
        ActivationContent.ExitRequested += OnCloseRequested;
        accountService.CurrentUserChanged += OnCurrentUserChanged;
    }

    private void UnregisterAuthenticationEvents()
    {
        Loaded -= OnAuthenticationLoaded;
        LoginContent.LoginRequested -= OnLoginRequested;
        LoginContent.ExitRequested -= OnCloseRequested;
        ActivationContent.ActivationRequested -= OnActivationRequested;
        ActivationContent.ExitRequested -= OnCloseRequested;
        accountService.CurrentUserChanged -= OnCurrentUserChanged;
    }

    private async void OnAuthenticationLoaded(object sender, RoutedEventArgs e)
    {
        using var timeout = new CancellationTokenSource(AuthenticationTimeout);
        LoginContent.IsEnabled = false;
        try
        {
            await TryInitializeAuditTrailAsync(timeout.Token);
            await softwareActivationService.InitializeAsync(timeout.Token);
            if (!softwareActivationService.IsActivated)
            {
                ActivationContent.ResetMessage();
                ActivationContent.Visibility = Visibility.Visible;
                ActivationContent.FocusActivationCode();
                return;
            }

            await CompleteLoginInitializationAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            logger.Warning("登录页初始化超时");
            ShowInitializationFailure("初始化超时，请重试；如仍失败请查看运行日志。");
        }
        catch (Exception exception)
        {
            logger.Error("登录页初始化失败", exception);
            ShowInitializationFailure($"初始化失败：{exception.Message}");
        }
    }

    private async Task TryInitializeAuditTrailAsync(CancellationToken cancellationToken)
    {
        try
        {
            await auditTrail.InitializeAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.Error("安全审计初始化失败，软件以降级模式继续运行", exception);
        }
    }

    private async void OnActivationRequested(object? sender, SoftwareActivationRequestedEventArgs e)
    {
        using var timeout = new CancellationTokenSource(AuthenticationTimeout);
        ActivationContent.SetBusy(true);
        try
        {
            var result = await softwareActivationService.ActivateAsync(e.ActivationCode, timeout.Token);
            ActivationContent.ClearActivationCode();
            if (!result.Succeeded)
            {
                ActivationContent.ShowMessage(result.Message, true);
                ActivationContent.FocusActivationCode();
                return;
            }

            ActivationContent.ShowMessage(result.Message, false);
            await CompleteLoginInitializationAsync(timeout.Token);
            _ = viewModel.TryAutomaticConnectionOnceAsync(automaticConnectionCts.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            ActivationContent.ShowMessage("激活操作超时，请重试", true);
        }
        catch (Exception exception)
        {
            logger.Error("软件激活失败", exception);
            ActivationContent.ShowMessage("激活失败，请重试", true);
        }
        finally
        {
            ActivationContent.SetBusy(false);
        }
    }

    private async Task CompleteLoginInitializationAsync(CancellationToken cancellationToken)
    {
        await accountService.InitializeAsync(cancellationToken);
        ActivationContent.Visibility = Visibility.Collapsed;
        LoginContent.IsEnabled = true;
        await ShowLoginContentAsync();
    }

    private void ShowInitializationFailure(string message)
    {
        if (softwareActivationService.IsActivated)
        {
            LoginContent.IsEnabled = true;
            LoginContent.ShowMessage(message, true);
            return;
        }

        ActivationContent.Visibility = Visibility.Visible;
        ActivationContent.ShowMessage(message, true);
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
        if (IsShutdownRequested
            || accountService.CurrentUser is not null
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
        ActivationContent.Visibility = Visibility.Collapsed;
        LoginContent.ClearPassword();

        var rememberedLoginName = await accountService.GetRememberedLoginNameAsync();
        LoginContent.ApplyRememberedLoginName(rememberedLoginName);
        LoginContent.ShowMessage(message ?? string.Empty, isError);
    }

    private void ShowMainContent()
    {
        LoginContent.Visibility = Visibility.Collapsed;
        MainContent.Visibility = Visibility.Visible;
        sessionSecurityService.NotifyUserActivity();
        logger.Info($"进入主界面：userId={accountService.CurrentUser?.UserId}");
    }

    private sealed record PasswordChangeOutcome(string Message, bool IsError);
}
