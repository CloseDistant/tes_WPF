namespace RuinaoHardwareDebugWpf;

using System.Windows;
using RuinaoHardwareDebugWpf.Views.Dialogs;

public sealed partial class MainViewModel
{
    private async Task InitializeAccountAsync()
    {
        try
        {
            await accountService.InitializeAsync();
            ShellState.FooterStatus = "账号服务已就绪";
        }
        catch (Exception ex)
        {
            logger.Error("账号服务初始化失败", ex);
            ShellState.FooterStatus = $"账号服务初始化失败：{ex.Message}";
        }
    }

    private async Task InitializePatientAsync()
    {
        try
        {
            await Patient.InitializeAsync();
            ShellState.FooterStatus = patientService.CurrentPatient is null ? "请新增或选择患者" : $"当前患者：{patientService.CurrentPatient.PatientCode}";
        }
        catch (Exception ex)
        {
            logger.Error("患者服务初始化失败", ex);
            ShellState.FooterStatus = $"患者服务初始化失败：{ex.Message}";
        }
    }

    private async Task CreatePatientAsync()
    {
        var dialog = new PatientFormDialog(null)
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };

        if (dialog.ShowDialog() != true || dialog.Request is null)
        {
            return;
        }

        if (!await EndSessionBeforePatientChangeAsync("新增并切换患者"))
        {
            return;
        }

        var patient = await patientService.CreatePatientAsync(dialog.Request);
        ShellState.FooterStatus = $"患者已新增并切换为当前患者：{patient.Name}";
    }

    private async Task EditPatientAsync()
    {
        var current = patientService.CurrentPatient;
        if (current is null)
        {
            ShellState.FooterStatus = "请先新增或选择患者";
            return;
        }

        var dialog = new PatientFormDialog(current)
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };

        if (dialog.ShowDialog() != true || dialog.Request is null)
        {
            return;
        }

        await patientService.UpdatePatientAsync(dialog.Request);
        ShellState.FooterStatus = "患者信息已保存。";
    }

    private async Task SwitchPatientAsync()
    {
        var patients = await patientService.GetPatientsAsync();
        if (patients.Count == 0)
        {
            ShellState.FooterStatus = "暂无历史患者，请先新增患者";
            return;
        }

        var dialog = new PatientSwitchDialog(patients, patientService.CurrentPatient?.PatientCode)
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };

        if (dialog.ShowDialog() != true || dialog.SelectedPatient is null)
        {
            return;
        }

        if (dialog.SelectedPatient.PatientCode == patientService.CurrentPatient?.PatientCode)
        {
            ShellState.FooterStatus = $"当前已是患者：{dialog.SelectedPatient.Name}。";
            return;
        }

        if (!await EndSessionBeforePatientChangeAsync("切换患者"))
        {
            return;
        }

        var patient = await patientService.SwitchCurrentPatientAsync(dialog.SelectedPatient.PatientCode);
        ShellState.FooterStatus = $"已切换到患者：{patient.Name}。";
    }

    private async Task EndCurrentSessionAsync()
    {
        var result = await sessionLifecycleCoordinator.EndCurrentAsync();
        ShellState.FooterStatus = result.Message;
    }

    private async Task<bool> EndSessionBeforePatientChangeAsync(string action)
    {
        var result = await sessionLifecycleCoordinator.PrepareForPatientChangeAsync(action);
        if (!result.Succeeded && !string.IsNullOrWhiteSpace(result.Message))
        {
            ShellState.FooterStatus = result.Message;
        }

        return result.Succeeded;
    }

    private void NotifyUnifiedSessionChanged()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(NotifyUnifiedSessionChanged);
            return;
        }

        OnPropertyChanged(nameof(CurrentSessionSummary));
        OnPropertyChanged(nameof(ActiveSessionVisibility));
    }

    private async Task LoginAsync()
    {
        await ShowLoginDialogAsync();
    }

    private async Task ForceChangePasswordAsync(CurrentUserInfo user)
    {
        string? error = null;
        while (true)
        {
            var dialog = new ChangePasswordDialog
            {
                Owner = System.Windows.Application.Current?.MainWindow
            };

            if (!string.IsNullOrWhiteSpace(error))
            {
                dialog.ErrorMessage = error;
            }

            if (dialog.ShowDialog() != true)
            {
                await accountService.LogoutAsync();
                ShellState.FooterStatus = "首次登录必须修改密码，请重新登录";
                return;
            }

            try
            {
                await accountService.ChangePasswordAsync(new ChangePasswordRequest(user.UserId, dialog.NewPassword, dialog.ConfirmPassword));
                ShellState.FooterStatus = "密码已修改，请重新登录";
                return;
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }
        }
    }

    private async Task RegisterAccountAsync()
    {
        if (!accountService.IsCurrentUserAdmin())
        {
            ShellState.FooterStatus = "只有 Admin 可以注册账号";
            return;
        }

        string? error = null;
        while (true)
        {
            var dialog = new AccountRegisterDialog
            {
                Owner = System.Windows.Application.Current?.MainWindow
            };

            if (!string.IsNullOrWhiteSpace(error))
            {
                dialog.ErrorMessage = error;
            }

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            try
            {
                var createdUser = await accountService.CreateUserAsync(dialog.Request);
                ShellState.FooterStatus = $"账号已创建：{createdUser.RoleName} {createdUser.DisplayName}";
                return;
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }
        }
    }

    private async Task SwitchAccountAsync()
    {
        var previousUser = accountService.CurrentUser;
        var newUser = await ShowLoginDialogAsync();
        if (previousUser is not null && newUser is not null && previousUser.UserId != newUser.UserId)
        {
            await accountService.RecordAuditAsync(previousUser.UserId, newUser.UserId, "switch_account", "success", "切换账号");
            AppendLog($"ACCOUNT switch from userId={previousUser.UserId} to userId={newUser.UserId}");
        }
    }

    private async Task LogoutAsync()
    {
        await accountService.LogoutAsync();
        ShellState.FooterStatus = "已退出登录";
    }

    private void NotifyAccountChanged()
    {
        OnPropertyChanged(nameof(IsLoggedIn));
        OnPropertyChanged(nameof(IsAdminLoggedIn));
        OnPropertyChanged(nameof(AccountMenuHeader));
        OnPropertyChanged(nameof(AccountMenuForeground));
        OnPropertyChanged(nameof(CurrentUserSummary));
        OnPropertyChanged(nameof(LoginMenuVisibility));
        OnPropertyChanged(nameof(LoggedInMenuVisibility));
        OnPropertyChanged(nameof(AdminMenuVisibility));
    }

    private async Task<CurrentUserInfo?> ShowLoginDialogAsync()
    {
        string? error = null;
        while (true)
        {
            var dialog = new AccountLoginDialog
            {
                Owner = System.Windows.Application.Current?.MainWindow
            };

            if (!string.IsNullOrWhiteSpace(error))
            {
                dialog.ShowError(error);
            }

            if (dialog.ShowDialog() != true)
            {
                return null;
            }

            var result = await accountService.LoginAsync(dialog.LoginName, dialog.Password);
            if (!result.Succeeded || result.User is null)
            {
                error = result.Message;
                continue;
            }

            ShellState.FooterStatus = result.Message;
            if (result.User.MustChangePassword)
            {
                await ForceChangePasswordAsync(result.User);
            }

            return result.User;
        }
    }

    /// <summary>
    /// 创建带统一异常处理的异步硬件命令。
    /// 异常会写入日志，并在底部状态栏显示错误信息。
    /// </summary>
}
