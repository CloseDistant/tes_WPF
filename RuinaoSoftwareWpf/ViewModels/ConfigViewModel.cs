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
    private readonly IToastService toastService;
    private readonly AsyncRelayCommand saveNavigationCommand;
    private readonly AsyncRelayCommand saveStimulationTypesCommand;
    private readonly AsyncRelayCommand saveStartupSettingsCommand;
    private readonly RelayCommand restoreStartupSettingsCommand;
    private readonly AsyncRelayCommand saveSessionSecurityCommand;
    private readonly RelayCommand decreaseIdleTimeoutCommand;
    private readonly RelayCommand increaseIdleTimeoutCommand;
    private bool stimulationSettingsRevealed;
    private int shiftPressCount;
    private DateTimeOffset? lastShiftPressAt;
    private string navigationStatus = string.Empty;
    private string stimulationStatus = string.Empty;
    private string startupSettingsStatus = string.Empty;
    private bool autoConnectOnStartup;
    private int idleTimeoutMinutes = ISessionSecurityService.DefaultIdleTimeoutMinutes;
    private string sessionSecurityStatus = string.Empty;

    public ConfigViewModel(
        IFeatureVisibilityService featureVisibilityService,
        IAccountService accountService,
        LocalizationViewModel localization,
        ILoggingService logger,
        IDesktopShortcutService desktopShortcutService,
        IStartupSettingsService startupSettingsService,
        ISessionSecurityService sessionSecurityService,
        IToastService toastService)
    {
        this.featureVisibilityService = featureVisibilityService;
        this.accountService = accountService;
        this.localization = localization;
        this.logger = logger;
        this.desktopShortcutService = desktopShortcutService;
        this.startupSettingsService = startupSettingsService;
        this.sessionSecurityService = sessionSecurityService;
        this.toastService = toastService;

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
            _ => IsAdmin && IdleTimeoutMinutes > ISessionSecurityService.MinimumIdleTimeoutMinutes);
        increaseIdleTimeoutCommand = new RelayCommand(
            _ => IdleTimeoutMinutes++,
            _ => IsAdmin && IdleTimeoutMinutes < ISessionSecurityService.MaximumIdleTimeoutMinutes);

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

        accountService.CurrentUserChanged += (_, _) => OnAccountChanged();
        featureVisibilityService.VisibilityChanged += (_, _) => ApplyPersistedVisibility();
        localization.PropertyChanged += OnLocalizationChanged;
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
                saveStimulationTypesCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public Visibility StimulationSettingsVisibility => IsAdmin && StimulationSettingsRevealed
        ? Visibility.Visible
        : Visibility.Collapsed;

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

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await featureVisibilityService.InitializeAsync(cancellationToken);
        await startupSettingsService.InitializeAsync(cancellationToken);
        await sessionSecurityService.InitializeAsync(cancellationToken);
        ApplyPersistedVisibility();
        AutoConnectOnStartup = startupSettingsService.AutoConnectOnStartup;
        IdleTimeoutMinutes = sessionSecurityService.IdleTimeoutMinutes;
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
        await sessionSecurityService.SaveIdleTimeoutAsync(IdleTimeoutMinutes, cancellationToken);
        SessionSecurityStatus = $"自动锁定时间已保存：{IdleTimeoutMinutes}分钟";
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
        OnPropertyChanged(nameof(NavigationSettingsVisibility));
        OnPropertyChanged(nameof(StimulationSettingsVisibility));
        saveNavigationCommand.RaiseCanExecuteChanged();
        saveStimulationTypesCommand.RaiseCanExecuteChanged();
        saveStartupSettingsCommand.RaiseCanExecuteChanged();
        restoreStartupSettingsCommand.RaiseCanExecuteChanged();
        saveSessionSecurityCommand.RaiseCanExecuteChanged();
        decreaseIdleTimeoutCommand.RaiseCanExecuteChanged();
        increaseIdleTimeoutCommand.RaiseCanExecuteChanged();
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
