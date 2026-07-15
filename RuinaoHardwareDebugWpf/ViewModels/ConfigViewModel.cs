namespace RuinaoHardwareDebugWpf;

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
    private readonly AsyncRelayCommand saveNavigationCommand;
    private readonly AsyncRelayCommand saveStimulationTypesCommand;
    private bool stimulationSettingsRevealed;
    private int shiftPressCount;
    private DateTimeOffset? lastShiftPressAt;
    private string navigationStatus = string.Empty;
    private string stimulationStatus = string.Empty;

    public ConfigViewModel(
        IFeatureVisibilityService featureVisibilityService,
        IAccountService accountService,
        LocalizationViewModel localization,
        ILoggingService logger)
    {
        this.featureVisibilityService = featureVisibilityService;
        this.accountService = accountService;
        this.localization = localization;
        this.logger = logger;

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

        SaveNavigationCommand = saveNavigationCommand;
        SaveStimulationTypesCommand = saveStimulationTypesCommand;
        RestoreNavigationCommand = new RelayCommand(_ => RestoreNavigationDefaults());
        RestoreStimulationTypesCommand = new RelayCommand(_ => RestoreStimulationDefaults());

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

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await featureVisibilityService.InitializeAsync(cancellationToken);
        ApplyPersistedVisibility();
    }

    public void EnterSettingsPage()
    {
        HideStimulationSettings();
        NavigationStatus = string.Empty;
        StimulationStatus = string.Empty;
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
