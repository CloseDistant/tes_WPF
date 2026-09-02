namespace RuinaoSoftwareWpf;

using System.Windows;
using RuinaoSoftwareWpf.Views.Dialogs;

public sealed partial class MainViewModel
{
    private void OnAssessmentPatientSelectionRequested(
        object? sender,
        AssessmentPatientSelectionRequestedEventArgs eventArgs)
    {
        eventArgs.IsHandled = true;
        eventArgs.Completion = SelectOrCreatePatientForAssessmentAsync(eventArgs.CancellationToken);
    }

    private void OnAssessmentPatientMatchingRequested(
        object? sender,
        AssessmentPatientMatchingRequestedEventArgs eventArgs)
    {
        eventArgs.IsHandled = true;
        eventArgs.Completion = AssessmentFeature.ShowMatchingAsync(eventArgs.CancellationToken);
    }

    private async Task SelectOrCreatePatientForAssessmentAsync(CancellationToken cancellationToken)
    {
        if (patientService.CurrentPatient is not null)
        {
            return;
        }

        if (!CanManagePatients)
        {
            toastService.ShowInformation("只有 Admin 或 Doctor 可以新增或选择患者。", "无患者管理权限");
            return;
        }

        if (IsPatientOperationLocked)
        {
            toastService.ShowInformation("当前模块正在运行或保存，不能切换患者。", "患者切换已禁用");
            return;
        }

        var firstPage = await patientService.GetPatientsPageAsync(
            new PageRequest(0, 30),
            cancellationToken);
        if (firstPage.Items.Count == 0)
        {
            var createDialog = new PatientFormDialog(null)
            {
                Owner = Application.Current?.MainWindow
            };
            if (createDialog.ShowDialog() != true || createDialog.Request is null)
            {
                return;
            }

            if (!await EndSessionBeforePatientChangeAsync("新增并切换患者"))
            {
                return;
            }

            var createdPatient = await patientService.CreatePatientAsync(
                createDialog.Request,
                cancellationToken);
            ShellState.FooterStatus = $"患者已新增并切换为当前患者：{createdPatient.Name}";
            return;
        }

        var switchDialog = new PatientSwitchDialog(
            patientService,
            firstPage,
            currentPatientCode: null)
        {
            Owner = Application.Current?.MainWindow
        };
        if (switchDialog.ShowDialog() != true || switchDialog.SelectedPatient is null)
        {
            return;
        }

        if (!await EndSessionBeforePatientChangeAsync("选择患者"))
        {
            return;
        }

        var selectedPatient = await patientService.SwitchCurrentPatientAsync(
            switchDialog.SelectedPatient.PatientCode,
            cancellationToken);
        ShellState.FooterStatus = $"已选择患者：{selectedPatient.Name}。";
    }

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
            await AssessmentFeature.ShowEntryAsync();
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
        if (!CanManagePatients)
        {
            ShellState.FooterStatus = "只有 Admin 或 Doctor 可以新增患者";
            return;
        }

        if (IsPatientOperationLocked)
        {
            toastService.ShowInformation("当前模块正在运行或保存，不能切换患者。", "患者切换已禁用");
            return;
        }

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
        await AssessmentFeature.ShowEntryAsync();
        ShellState.FooterStatus = $"患者已新增并切换为当前患者：{patient.Name}";
    }

    private async Task EditPatientAsync()
    {
        if (!CanManagePatients)
        {
            ShellState.FooterStatus = "只有 Admin 或 Doctor 可以编辑患者信息";
            return;
        }

        var current = patientService.CurrentPatient;
        if (current is null)
        {
            toastService.ShowInformation("请选择患者");
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
        if (!CanManagePatients)
        {
            ShellState.FooterStatus = "只有 Admin 或 Doctor 可以切换患者";
            return;
        }

        if (IsPatientOperationLocked)
        {
            toastService.ShowInformation("当前模块正在运行或保存，不能切换患者。", "患者切换已禁用");
            return;
        }

        var firstPage = await patientService.GetPatientsPageAsync(new PageRequest(0, 30));
        if (firstPage.Items.Count == 0)
        {
            toastService.ShowInformation("请先添加患者");
            return;
        }

        var dialog = new PatientSwitchDialog(
            patientService,
            firstPage,
            patientService.CurrentPatient?.PatientCode)
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
        await AssessmentFeature.ShowEntryAsync();
        ShellState.FooterStatus = $"已切换到患者：{patient.Name}。";
    }

    private async Task EndCurrentSessionAsync()
    {
        var result = await sessionLifecycleCoordinator.EndCurrentAsync();
        if (result.Confirmation is { } confirmation)
        {
            if (!ConfirmSessionLifecycle(confirmation))
            {
                ShellState.FooterStatus = confirmation.CancelledResultMessage;
                return;
            }

            result = await sessionLifecycleCoordinator.EndCurrentAsync(
                confirmation.SessionKey);
        }

        ShellState.FooterStatus = result.Message;
    }

    private async Task<bool> EndSessionBeforePatientChangeAsync(string action)
    {
        var result = await sessionLifecycleCoordinator.PrepareForPatientChangeAsync(action);
        if (result.Confirmation is { } confirmation)
        {
            if (!ConfirmSessionLifecycle(confirmation))
            {
                ShellState.FooterStatus = confirmation.CancelledResultMessage;
                return false;
            }

            result = await sessionLifecycleCoordinator.PrepareForPatientChangeAsync(
                action,
                confirmation.SessionKey);
        }

        if (!result.Succeeded && !string.IsNullOrWhiteSpace(result.Message))
        {
            ShellState.FooterStatus = result.Message;
        }

        return result.Succeeded;
    }

    private bool ConfirmSessionLifecycle(SessionLifecycleConfirmationRequest confirmation)
    {
        return userDialogService.ConfirmWarning(
            confirmation.Title,
            confirmation.Message,
            confirmation.ConfirmText,
            confirmation.CancelText);
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

    private void NotifyPatientMenuAvailabilityChanged()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(NotifyPatientMenuAvailabilityChanged);
            return;
        }

        OnPropertyChanged(nameof(IsPatientMenuEnabled));
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
            var changePassword = userDialogService.RequestPasswordChange(error);
            if (changePassword is null)
            {
                await accountService.LogoutAsync();
                ResetStimulationNavigation();
                ShellState.FooterStatus = "首次登录必须修改密码，请重新登录";
                return;
            }

            try
            {
                await accountService.ChangePasswordAsync(new ChangePasswordRequest(
                    user.UserId,
                    changePassword.NewPassword,
                    changePassword.ConfirmPassword));
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

    private Task ViewAccountListAsync()
    {
        if (!accountService.IsCurrentUserAdmin())
        {
            ShellState.FooterStatus = "只有 Admin 可以查看账号列表";
            return Task.CompletedTask;
        }

        var dialog = new AccountListDialog(accountService)
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        dialog.ShowDialog();
        return Task.CompletedTask;
    }

    private Task OpenAuditTrailAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (accountService.CurrentUser is null)
        {
            ShellState.FooterStatus = "请先登录后再访问安全审计";
            return Task.CompletedTask;
        }

        var dialog = new AuditTrailDialog(AuditTrail)
        {
            Owner = Application.Current?.MainWindow
        };
        dialog.ShowDialog();
        return Task.CompletedTask;
    }

    private async Task SwitchAccountAsync()
    {
        var previousUser = accountService.CurrentUser;
        var newUser = await ShowLoginDialogAsync();
        if (previousUser is not null && newUser is not null && previousUser.UserId != newUser.UserId)
        {
            ResetStimulationNavigation();
            await accountService.RecordAuditAsync(previousUser.UserId, newUser.UserId, "switch_account", "success", "切换账号");
            AppendLog($"ACCOUNT switch from userId={previousUser.UserId} to userId={newUser.UserId}");
        }
    }

    private async Task LogoutAsync()
    {
        await accountService.LogoutAsync();
        ResetStimulationNavigation();
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
        OnPropertyChanged(nameof(AuditMenuVisibility));
        openAuditTrailCommand.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(CanManagePatients));
        OnPropertyChanged(nameof(PatientMenuVisibility));
        NotifyPatientMenuAvailabilityChanged();
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
