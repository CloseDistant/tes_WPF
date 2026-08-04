namespace RuinaoSoftwareWpf;

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;

public sealed class ConfigViewModel : ObservableObject
{
    private static readonly TimeSpan ShiftSequenceWindow = TimeSpan.FromSeconds(2);

    private readonly IFeatureVisibilityService featureVisibilityService;
    private readonly IAccountService accountService;
    private readonly LocalizationViewModel localization;
    private readonly ILoggingService logger;
    private readonly IDesktopShortcutService desktopShortcutService;
    private readonly IStartupSettingsService startupSettingsService;
    private readonly ISessionSecurityService sessionSecurityService;
    private readonly IHardwareService hardwareService;
    private readonly IDebugHardwareSimulationService debugHardwareSimulation;
    private readonly IToastService toastService;
    private readonly IIntegrityCheckService integrityCheckService;
    private readonly IBackupRestoreService backupRestoreService;
    private readonly IUserDialogService userDialogService;
    private readonly AsyncRelayCommand saveNavigationCommand;
    private readonly AsyncRelayCommand saveStimulationTypesCommand;
    private readonly AsyncRelayCommand saveStartupSettingsCommand;
    private readonly RelayCommand restoreStartupSettingsCommand;
    private readonly AsyncRelayCommand saveSessionSecurityCommand;
    private readonly RelayCommand decreaseIdleTimeoutCommand;
    private readonly RelayCommand increaseIdleTimeoutCommand;
    private readonly RelayCommand connectDebugSimulationCommand;
    private bool stimulationSettingsRevealed;
    private int shiftPressCount;
    private DateTimeOffset? lastShiftPressAt;
    private string navigationStatus = string.Empty;
    private string stimulationStatus = string.Empty;
    private string startupSettingsStatus = string.Empty;
    private bool autoConnectOnStartup;
    private bool isAutoLockEnabled = true;
    private int idleTimeoutMinutes = ISessionSecurityService.DefaultIdleTimeoutMinutes;
    private string sessionSecurityStatus = string.Empty;
    private string releaseIntegrityStatusText = "尚未校验";
    private string releaseIntegrityTimeText = "--";

    public ConfigViewModel(
        IFeatureVisibilityService featureVisibilityService,
        IAccountService accountService,
        LocalizationViewModel localization,
        ILoggingService logger,
        IDesktopShortcutService desktopShortcutService,
        IStartupSettingsService startupSettingsService,
        ISessionSecurityService sessionSecurityService,
        IHardwareService hardwareService,
        IDebugHardwareSimulationService debugHardwareSimulation,
        IToastService toastService,
        IIntegrityCheckService integrityCheckService,
        IBackupRestoreService backupRestoreService,
        IUserDialogService userDialogService)
    {
        this.featureVisibilityService = featureVisibilityService;
        this.accountService = accountService;
        this.localization = localization;
        this.logger = logger;
        this.desktopShortcutService = desktopShortcutService;
        this.startupSettingsService = startupSettingsService;
        this.sessionSecurityService = sessionSecurityService;
        this.hardwareService = hardwareService;
        this.debugHardwareSimulation = debugHardwareSimulation;
        this.toastService = toastService;
        this.integrityCheckService = integrityCheckService;
        this.backupRestoreService = backupRestoreService;
        this.userDialogService = userDialogService;

        NavigationOptions = new ObservableCollection<FeatureVisibilityOptionViewModel>(
            FeatureCatalog.Navigation.Select((item, index) => CreateNavigationOption(item, index)));
        StimulationTypeOptions = new ObservableCollection<FeatureVisibilityOptionViewModel>(
            FeatureCatalog.StimulationTypes.Select((item, index) => CreateStimulationOption(item, index)));

        saveNavigationCommand = new AsyncRelayCommand(
            SaveNavigationAsync,
            () => IsAdmin,
            exception => HandleSaveError(exception, isStimulation: false));
        saveStimulationTypesCommand = new AsyncRelayCommand(
            SaveStimulationTypesAsync,
            () => IsAdmin && StimulationSettingsRevealed,
            exception => HandleSaveError(exception, isStimulation: true));
        saveStartupSettingsCommand = new AsyncRelayCommand(
            SaveStartupSettingsAsync,
            () => IsAdmin,
            onError: HandleStartupSettingsSaveError);
        restoreStartupSettingsCommand = new RelayCommand(
            _ => RestoreStartupSettingsDefaults(),
            _ => IsAdmin);
        saveSessionSecurityCommand = new AsyncRelayCommand(
            SaveSessionSecurityAsync,
            () => IsAdmin,
            HandleSessionSecuritySaveError);
        decreaseIdleTimeoutCommand = new RelayCommand(
            _ => IdleTimeoutMinutes--,
            _ => CanEditIdleTimeout
                && IdleTimeoutMinutes > ISessionSecurityService.MinimumIdleTimeoutMinutes);
        increaseIdleTimeoutCommand = new RelayCommand(
            _ => IdleTimeoutMinutes++,
            _ => CanEditIdleTimeout
                && IdleTimeoutMinutes < ISessionSecurityService.MaximumIdleTimeoutMinutes);
        connectDebugSimulationCommand = new RelayCommand(
            _ => ConnectDebugSimulation(),
            _ => CanConnectDebugSimulation());

        SaveNavigationCommand = saveNavigationCommand;
        SaveStimulationTypesCommand = saveStimulationTypesCommand;
        RestoreNavigationCommand = new RelayCommand(_ => RestoreNavigationDefaults());
        RestoreStimulationTypesCommand = new RelayCommand(_ => RestoreStimulationDefaults());
        CreateDesktopShortcutCommand = new RelayCommand(_ => CreateDesktopShortcut());
        SaveStartupSettingsCommand = saveStartupSettingsCommand;
        RestoreStartupSettingsCommand = restoreStartupSettingsCommand;
        SaveSessionSecurityCommand = saveSessionSecurityCommand;
        RestoreSessionSecurityCommand = new RelayCommand(_ => RestoreSessionSecurityDefaults());
        DecreaseIdleTimeoutCommand = decreaseIdleTimeoutCommand;
        IncreaseIdleTimeoutCommand = increaseIdleTimeoutCommand;
        ConnectDebugSimulationCommand = connectDebugSimulationCommand;

        accountService.CurrentUserChanged += (_, _) => OnAccountChanged();
        featureVisibilityService.VisibilityChanged += (_, _) => ApplyPersistedVisibility();
        localization.PropertyChanged += OnLocalizationChanged;
        debugHardwareSimulation.ConnectionChanged += (_, _) => OnDebugSimulationChanged();
        hardwareService.ConnectionChanged += (_, _) => connectDebugSimulationCommand.RaiseCanExecuteChanged();
    }

    public ObservableCollection<FeatureVisibilityOptionViewModel> NavigationOptions { get; }

    public ObservableCollection<FeatureVisibilityOptionViewModel> StimulationTypeOptions { get; }

    public ICommand SaveNavigationCommand { get; }

    public ICommand RestoreNavigationCommand { get; }

    public ICommand SaveStimulationTypesCommand { get; }

    public ICommand RestoreStimulationTypesCommand { get; }

    public ICommand CreateDesktopShortcutCommand { get; }

    public ICommand SaveStartupSettingsCommand { get; }

    public ICommand RestoreStartupSettingsCommand { get; }

    public ICommand SaveSessionSecurityCommand { get; }

    public ICommand RestoreSessionSecurityCommand { get; }

    public ICommand DecreaseIdleTimeoutCommand { get; }

    public ICommand IncreaseIdleTimeoutCommand { get; }

    public ICommand ConnectDebugSimulationCommand { get; }

    public bool IsAdmin => accountService.CurrentUser?.RoleId == AccountRoles.Admin;

    public Visibility NavigationSettingsVisibility => IsAdmin && StimulationSettingsRevealed
        ? Visibility.Visible
        : Visibility.Collapsed;

    public bool AllNavigationVisible
    {
        get => NavigationOptions.All(item => item.IsVisible);
        set => SetAllVisibility(NavigationOptions, value);
    }

    public bool AllStimulationTypesVisible
    {
        get => StimulationTypeOptions.All(item => item.IsVisible);
        set => SetAllVisibility(StimulationTypeOptions, value);
    }

    public bool StimulationSettingsRevealed
    {
        get => stimulationSettingsRevealed;
        private set
        {
            if (SetProperty(ref stimulationSettingsRevealed, value))
            {
                OnPropertyChanged(nameof(NavigationSettingsVisibility));
                OnPropertyChanged(nameof(StimulationSettingsVisibility));
                OnPropertyChanged(nameof(DebugSimulationVisibility));
                saveStimulationTypesCommand.RaiseCanExecuteChanged();
                connectDebugSimulationCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public Visibility StimulationSettingsVisibility => IsAdmin && StimulationSettingsRevealed
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility DebugSimulationVisibility => debugHardwareSimulation.IsAvailable
        && IsAdmin
        && StimulationSettingsRevealed
            ? Visibility.Visible
            : Visibility.Collapsed;

    public bool IsDebugSimulationConnected => debugHardwareSimulation.IsConnected;

    public string DebugSimulationStatus => IsDebugSimulationConnected ? "已启用" : "未启用";

    public string NavigationStatus
    {
        get => navigationStatus;
        private set => SetProperty(ref navigationStatus, value);
    }

    public string StimulationStatus
    {
        get => stimulationStatus;
        private set => SetProperty(ref stimulationStatus, value);
    }

    public bool AutoConnectOnStartup
    {
        get => autoConnectOnStartup;
        set => SetProperty(ref autoConnectOnStartup, value);
    }

    public string StartupSettingsStatus
    {
        get => startupSettingsStatus;
        private set => SetProperty(ref startupSettingsStatus, value);
    }

    public bool IsAutoLockEnabled
    {
        get => isAutoLockEnabled;
        set
        {
            if (SetProperty(ref isAutoLockEnabled, value))
            {
                OnPropertyChanged(nameof(AutoLockStateText));
                OnPropertyChanged(nameof(CanEditIdleTimeout));
                decreaseIdleTimeoutCommand.RaiseCanExecuteChanged();
                increaseIdleTimeoutCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string AutoLockStateText => IsAutoLockEnabled ? "已启用" : "未开启";

    public bool CanEditIdleTimeout => IsAdmin && IsAutoLockEnabled;

    public int IdleTimeoutMinutes
    {
        get => idleTimeoutMinutes;
        set
        {
            var normalized = Math.Clamp(
                value,
                ISessionSecurityService.MinimumIdleTimeoutMinutes,
                ISessionSecurityService.MaximumIdleTimeoutMinutes);
            if (SetProperty(ref idleTimeoutMinutes, normalized))
            {
                decreaseIdleTimeoutCommand.RaiseCanExecuteChanged();
                increaseIdleTimeoutCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string SessionSecurityStatus
    {
        get => sessionSecurityStatus;
        private set => SetProperty(ref sessionSecurityStatus, value);
    }

    public string ReleaseIntegrityStatusText
    {
        get => releaseIntegrityStatusText;
        private set => SetProperty(ref releaseIntegrityStatusText, value);
    }

    public string ReleaseIntegrityTimeText
    {
        get => releaseIntegrityTimeText;
        private set => SetProperty(ref releaseIntegrityTimeText, value);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await featureVisibilityService.InitializeAsync(cancellationToken);
        await startupSettingsService.InitializeAsync(cancellationToken);
        await sessionSecurityService.InitializeAsync(cancellationToken);
        ApplyPersistedVisibility();
        AutoConnectOnStartup = startupSettingsService.AutoConnectOnStartup;
        IsAutoLockEnabled = sessionSecurityService.IsAutoLockEnabled;
        IdleTimeoutMinutes = sessionSecurityService.IdleTimeoutMinutes;
        await TryRefreshReleaseIntegrityStatusAsync(cancellationToken);
    }

    public async Task<IntegrityCheckResult> CheckReleaseFilesAsync(
        IProgress<IntegrityCheckProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ReleaseIntegrityStatusText = "正在校验…";
        try
        {
            var result = await integrityCheckService.CheckReleaseFilesAsync(
                progress,
                cancellationToken);
            await RefreshReleaseIntegrityStatusAsync(cancellationToken);
            return result;
        }
        catch
        {
            await TryRefreshReleaseIntegrityStatusAsync(CancellationToken.None);
            throw;
        }
    }

    public void NotifyIntegrityCheckCanceled()
    {
        toastService.ShowInformation("校验已取消", "已保留上一次校验结果。");
    }

    public Task<BackupLocationInfo> GetDefaultBackupLocationAsync(
        CancellationToken cancellationToken = default)
    {
        return backupRestoreService.GetDefaultBackupLocationAsync(cancellationToken);
    }

    public Task<BackupStatus> GetBackupStatusAsync(CancellationToken cancellationToken = default)
    {
        return backupRestoreService.GetStatusAsync(cancellationToken);
    }

    public Task<BackupOperationResult> CreateBackupAsync(
        string targetDirectory,
        string password,
        IProgress<BackupOperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return backupRestoreService.CreateBackupAsync(
            targetDirectory,
            password,
            progress,
            cancellationToken);
    }

    public Task<BackupOperationResult> RestoreBackupAsync(
        string backupFile,
        string password,
        IProgress<BackupOperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return backupRestoreService.RestoreBackupAsync(
            backupFile,
            password,
            progress,
            cancellationToken);
    }

    public bool ConfirmDataRestore()
    {
        return userDialogService.ConfirmWarning(
            "确认恢复数据",
            "恢复将使用备份内容替换本机业务数据和安全审计数据。是否继续？",
            "继续恢复",
            "取消");
    }

    public void NotifyBackupSucceeded(string? filePath)
    {
        toastService.ShowSuccess("数据备份完成", $"已保存：{System.IO.Path.GetFileName(filePath)}");
    }

    public void NotifyBackupFailed(Exception exception)
    {
        toastService.ShowError("数据备份失败", exception.Message);
    }

    public void NotifyRestoreFailed(Exception exception)
    {
        toastService.ShowError("数据恢复失败", exception.Message);
    }

    private async Task TryRefreshReleaseIntegrityStatusAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            await RefreshReleaseIntegrityStatusAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.Error("读取发布文件校验状态失败", exception);
            ReleaseIntegrityStatusText = "校验状态读取失败";
            ReleaseIntegrityTimeText = "--";
        }
    }

    private async Task RefreshReleaseIntegrityStatusAsync(
        CancellationToken cancellationToken)
    {
        var status = await integrityCheckService.GetReleaseStatusAsync(cancellationToken);
        ReleaseIntegrityStatusText = status.Kind switch
        {
            ReleaseIntegrityStatusKind.Passed => "上次校验通过",
            ReleaseIntegrityStatusKind.Failed => "上次校验失败",
            ReleaseIntegrityStatusKind.ReleaseChanged => "发布文件已变化，需重新校验",
            _ => "尚未校验"
        };
        ReleaseIntegrityTimeText = status.LastResult is { } lastResult
            ? $"校验时间：{lastResult.CompletedAt:yyyy-MM-dd HH:mm}"
            : "--";
    }

    private void CreateDesktopShortcut()
    {
        var result = desktopShortcutService.CreateOrUpdate();
        if (result.Succeeded)
        {
            toastService.ShowSuccess("快捷方式已创建", "桌面快捷方式已创建或更新。");
            return;
        }

        logger.Warning(result.Message);
        toastService.ShowError("快捷方式创建失败", result.Message);
    }

    private bool CanConnectDebugSimulation()
    {
        return debugHardwareSimulation.IsAvailable
            && IsAdmin
            && StimulationSettingsRevealed
            && !debugHardwareSimulation.IsConnected
            && !hardwareService.IsConnected
            && !hardwareService.IsConnecting;
    }

    private void ConnectDebugSimulation()
    {
        var result = debugHardwareSimulation.Connect(hardwareService.IsConnected);
        if (result.Succeeded)
        {
            logger.Debug("DEBUG 模拟联机由管理员手动启用");
            toastService.ShowSuccess("设备联机成功", "16个刺激通道已就绪。");
            return;
        }

        toastService.Show(ToastKind.Warning, "设备联机失败", result.Message);
    }

    private void OnDebugSimulationChanged()
    {
        OnPropertyChanged(nameof(IsDebugSimulationConnected));
        OnPropertyChanged(nameof(DebugSimulationStatus));
        connectDebugSimulationCommand.RaiseCanExecuteChanged();
    }

    public void EnterSettingsPage()
    {
        HideStimulationSettings();
        NavigationStatus = string.Empty;
        StimulationStatus = string.Empty;
        StartupSettingsStatus = string.Empty;
        SessionSecurityStatus = string.Empty;
    }

    public void LeaveSettingsPage()
    {
        HideStimulationSettings();
    }

    public void RegisterShiftPress(DateTimeOffset pressedAt)
    {
        if (!IsAdmin || StimulationSettingsRevealed)
        {
            return;
        }

        if (lastShiftPressAt is null || pressedAt - lastShiftPressAt > ShiftSequenceWindow)
        {
            shiftPressCount = 0;
        }

        shiftPressCount++;
        lastShiftPressAt = pressedAt;
        if (shiftPressCount >= 3)
        {
            StimulationSettingsRevealed = true;
            shiftPressCount = 0;
            lastShiftPressAt = null;
        }
    }

    public void ResetShiftSequence()
    {
        shiftPressCount = 0;
        lastShiftPressAt = null;
    }

    private FeatureVisibilityOptionViewModel CreateNavigationOption(
        NavigationFeatureDefinition definition,
        int index)
    {
        return CreateOption(
            definition.Key,
            definition.LocalizationKey,
            string.Empty,
            index,
            definition.DefaultVisible);
    }

    private FeatureVisibilityOptionViewModel CreateStimulationOption(
        StimulationTypeFeatureDefinition definition,
        int index)
    {
        return CreateOption(
            definition.Key,
            definition.LocalizationKey,
            definition.ShortName,
            index,
            definition.DefaultVisible);
    }

    private FeatureVisibilityOptionViewModel CreateOption(
        string key,
        string localizationKey,
        string shortName,
        int index,
        bool defaultVisible)
    {
        var option = new FeatureVisibilityOptionViewModel(
            key,
            localizationKey,
            $"{index + 1:00}",
            localization.FeatureText(localizationKey),
            shortName,
            defaultVisible);
        option.PropertyChanged += OnOptionPropertyChanged;
        return option;
    }

    private async Task SaveNavigationAsync(CancellationToken cancellationToken)
    {
        await featureVisibilityService.SaveAsync(
            NavigationOptions.ToDictionary(item => item.Key, item => item.IsVisible, StringComparer.Ordinal),
            cancellationToken);
        NavigationStatus = "导航栏显示设置已保存";
    }

    private async Task SaveStimulationTypesAsync(CancellationToken cancellationToken)
    {
        await featureVisibilityService.SaveAsync(
            StimulationTypeOptions.ToDictionary(item => item.Key, item => item.IsVisible, StringComparer.Ordinal),
            cancellationToken);
        StimulationStatus = "电刺激类型显示设置已保存";
    }

    private async Task SaveStartupSettingsAsync(CancellationToken cancellationToken)
    {
        await startupSettingsService.SaveAutoConnectOnStartupAsync(
            AutoConnectOnStartup,
            cancellationToken);
        StartupSettingsStatus = "启动设置已保存，下次启动时生效";
    }

    private async Task SaveSessionSecurityAsync(CancellationToken cancellationToken)
    {
        await sessionSecurityService.SaveAutoLockSettingsAsync(
            IsAutoLockEnabled,
            IdleTimeoutMinutes,
            cancellationToken);
        SessionSecurityStatus = IsAutoLockEnabled
            ? $"自动锁定已开启：{IdleTimeoutMinutes}分钟"
            : "自动锁定已关闭";
    }

    private void RestoreNavigationDefaults()
    {
        foreach (var option in NavigationOptions)
        {
            option.IsVisible = FeatureCatalog.DefaultVisibility(option.Key);
        }

        NavigationStatus = "已恢复默认，点击保存后生效";
    }

    private void RestoreStimulationDefaults()
    {
        foreach (var option in StimulationTypeOptions)
        {
            option.IsVisible = FeatureCatalog.DefaultVisibility(option.Key);
        }

        StimulationStatus = "已恢复默认，点击保存后生效";
    }

    private void RestoreStartupSettingsDefaults()
    {
        AutoConnectOnStartup = false;
        StartupSettingsStatus = "已恢复默认，点击保存后生效";
    }

    private void RestoreSessionSecurityDefaults()
    {
        IsAutoLockEnabled = true;
        IdleTimeoutMinutes = ISessionSecurityService.DefaultIdleTimeoutMinutes;
        SessionSecurityStatus = "已恢复默认，点击保存后生效";
    }

    private void ApplyPersistedVisibility()
    {
        foreach (var option in NavigationOptions.Concat(StimulationTypeOptions))
        {
            option.IsVisible = featureVisibilityService.IsVisible(option.Key);
        }
    }

    private void SetAllVisibility(IEnumerable<FeatureVisibilityOptionViewModel> options, bool isVisible)
    {
        foreach (var option in options)
        {
            option.IsVisible = isVisible;
        }
    }

    private void OnOptionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(FeatureVisibilityOptionViewModel.IsVisible))
        {
            return;
        }

        OnPropertyChanged(nameof(AllNavigationVisible));
        OnPropertyChanged(nameof(AllStimulationTypesVisible));
    }

    private void OnLocalizationChanged(object? sender, PropertyChangedEventArgs e)
    {
        foreach (var option in NavigationOptions.Concat(StimulationTypeOptions))
        {
            option.DisplayName = localization.FeatureText(option.LocalizationKey);
        }
    }

    private void OnAccountChanged()
    {
        HideStimulationSettings();
        OnPropertyChanged(nameof(IsAdmin));
        OnPropertyChanged(nameof(CanEditIdleTimeout));
        OnPropertyChanged(nameof(NavigationSettingsVisibility));
        OnPropertyChanged(nameof(StimulationSettingsVisibility));
        OnPropertyChanged(nameof(DebugSimulationVisibility));
        saveNavigationCommand.RaiseCanExecuteChanged();
        saveStimulationTypesCommand.RaiseCanExecuteChanged();
        saveStartupSettingsCommand.RaiseCanExecuteChanged();
        restoreStartupSettingsCommand.RaiseCanExecuteChanged();
        saveSessionSecurityCommand.RaiseCanExecuteChanged();
        decreaseIdleTimeoutCommand.RaiseCanExecuteChanged();
        increaseIdleTimeoutCommand.RaiseCanExecuteChanged();
        connectDebugSimulationCommand.RaiseCanExecuteChanged();
    }

    private void HideStimulationSettings()
    {
        StimulationSettingsRevealed = false;
        ResetShiftSequence();
    }

    private void HandleSaveError(Exception exception, bool isStimulation)
    {
        logger.Error("保存功能显示设置失败", exception);
        if (isStimulation)
        {
            StimulationStatus = exception.Message;
        }
        else
        {
            NavigationStatus = exception.Message;
        }
    }

    private void HandleStartupSettingsSaveError(Exception exception)
    {
        logger.Error("保存启动设置失败", exception);
        StartupSettingsStatus = exception.Message;
    }

    private void HandleSessionSecuritySaveError(Exception exception)
    {
        logger.Error("保存会话安全设置失败", exception);
        SessionSecurityStatus = exception.Message;
    }
}

public sealed class FeatureVisibilityOptionViewModel : ObservableObject
{
    private string displayName;
    private bool isVisible;

    public FeatureVisibilityOptionViewModel(
        string key,
        string localizationKey,
        string orderText,
        string displayName,
        string shortName,
        bool isVisible)
    {
        Key = key;
        LocalizationKey = localizationKey;
        OrderText = orderText;
        this.displayName = displayName;
        ShortName = shortName;
        this.isVisible = isVisible;
    }

    public string Key { get; }

    public string LocalizationKey { get; }

    public string OrderText { get; }

    public string ShortName { get; }

    public string DisplayName
    {
        get => displayName;
        set => SetProperty(ref displayName, value);
    }

    public bool IsVisible
    {
        get => isVisible;
        set => SetProperty(ref isVisible, value);
    }
}
