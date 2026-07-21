using System.Windows.Input;
using System.Windows;
using System.Windows.Controls;
using RuinaoSoftwareWpf.Views.Dialogs;

namespace RuinaoSoftwareWpf;

/// <summary>
/// 主界面的状态中心。
///
/// 负责：
/// - 管理页面导航，左侧导航点击后切换 CurrentPageViewModel。
/// - 将顶部设备菜单命令转发给 HardwareService 执行硬件操作。
/// - 聚合多语言、患者信息、底部状态栏和各业务页面 ViewModel。
///
/// 日志不再显示在主界面；Debug 构建会输出到独立控制台窗口。
/// </summary>
public sealed partial class MainViewModel : ObservableObject, IMainUiContext
{
    private readonly IHardwareService hardwareService;
    private readonly ILoggingService logger;
    private readonly IUserDialogService userDialogService;
    private readonly IAccountService accountService;
    private readonly IFeatureVisibilityService featureVisibilityService;
    private readonly IStartupSettingsService startupSettingsService;
    private readonly ISoftwareActivationService softwareActivationService;
    private readonly IPatientService patientService;
    private readonly IStimulationStateMachine stimulationStateMachine;
    private readonly ISessionLifecycleCoordinator sessionLifecycleCoordinator;
    private readonly IDebugHardwareSimulationService debugHardwareSimulation;
    private readonly IToastService toastService;
    private readonly AsyncRelayCommand connectCommand;
    private readonly AsyncRelayCommand openAuditTrailCommand;

    private AppPage currentPage = AppPage.Control;
    private ObservableObject? currentPageViewModel;
    private bool isSidebarCollapsed;
    private readonly SemaphoreSlim shutdownGate = new(1, 1);
    private bool shutdownCompleted;
    private Task initializationTask = Task.CompletedTask;
    private int automaticConnectionAttempted;

    /// <summary>
    /// 构造函数由 DI 容器注入所需服务和子 ViewModel。
    /// </summary>
    public MainViewModel(
        IHardwareService hardwareService,
        ILoggingService logger,
        IUserDialogService userDialogService,
        IAccountService accountService,
        IFeatureVisibilityService featureVisibilityService,
        IStartupSettingsService startupSettingsService,
        ISoftwareActivationService softwareActivationService,
        IPatientService patientService,
        IStimulationStateMachine stimulationStateMachine,
        ISessionLifecycleCoordinator sessionLifecycleCoordinator,
        IDebugHardwareSimulationService debugHardwareSimulation,
        IToastService toastService,
        AuditTrailViewModel auditTrail,
        NavigationViewModel navigation,
        LocalizationViewModel localization,
        PatientViewModel patient,
        ShellStateViewModel shellState,
        MonitorViewModel monitor,
        StimulationTypeSelectionViewModel stimulationTypeSelection,
        TiControlViewModel tiControl,
        DirectCurrentControlViewModel directCurrentControl,
        PrescriptionViewModel prescription,
        EegSignalCaptureViewModel eegSignalCapture,
        AssessmentCaptureViewModel assessmentCapture,
        FemSimulationViewModel femSimulation,
        DeviceViewModel device,
        ConfigViewModel config,
        ReportViewModel report,
        PlaceholderPageViewModel placeholderPage)
    {
        this.hardwareService = hardwareService;
        this.logger = logger;
        this.userDialogService = userDialogService;
        this.accountService = accountService;
        this.featureVisibilityService = featureVisibilityService;
        this.startupSettingsService = startupSettingsService;
        this.softwareActivationService = softwareActivationService;
        this.patientService = patientService;
        this.stimulationStateMachine = stimulationStateMachine;
        this.sessionLifecycleCoordinator = sessionLifecycleCoordinator;
        this.debugHardwareSimulation = debugHardwareSimulation;
        this.toastService = toastService;
        AuditTrail = auditTrail;

        Navigation = navigation;
        Localization = localization;
        Patient = patient;
        ShellState = shellState;
        Monitor = monitor;
        StimulationTypeSelection = stimulationTypeSelection;
        TiControl = tiControl;
        DirectCurrentControl = directCurrentControl;
        Prescription = prescription;
        EegSignalCapture = eegSignalCapture;
        AssessmentCapture = assessmentCapture;
        FemSimulation = femSimulation;
        Device = device;
        Config = config;
        Report = report;
        PlaceholderPage = placeholderPage;

        BuildNavigationItems();

        AppendLog("UI READY  hardware debug prototype");
        AppendLog("PROTO DLL READY  RuinaoTesProtocol referenced");

        // 设备菜单操作复用统一 Toast；异步命令在收到结果前会自动禁用对应按钮。
        connectCommand = new AsyncRelayCommand(
            ConnectDeviceAsync,
            canExecute: CanConnectDevice,
            onError: exception => HandleDeviceOperationError(
                "联机失败",
                "设备联机失败，请检查设备连接和通讯配置后重试。",
                exception));
        ConnectCommand = connectCommand;
        DisconnectCommand = new AsyncRelayCommand(
            DisconnectDeviceAsync,
            onError: exception => HandleDeviceOperationError(
                "断开失败",
                "设备断开失败，请检查通讯状态后重试。",
                exception));
        HandshakeCommand = new AsyncRelayCommand(
            HandshakeDeviceAsync,
            onError: exception => HandleDeviceOperationError(
                "握手检测失败",
                "未收到有效的握手反馈，请检查设备连接后重试。",
                exception));
        ReadProductModelCommand = CreateHardwareCommand(_ => ReadProductModelAsync());
        ReadBoardModelCommand = CreateHardwareCommand(_ => ReadBoardModelAsync());
        CheckImpedanceCommand = new AsyncRelayCommand(
            CheckImpedanceAsync,
            onError: HandleImpedanceRefreshError);
        openAuditTrailCommand = new AsyncRelayCommand(
            OpenAuditTrailAsync,
            () => IsLoggedIn,
            exception => HandleDeviceOperationError("安全审计打开失败", "无法打开安全审计，请稍后重试。", exception));
        OpenAuditTrailCommand = openAuditTrailCommand;

        ExitCommand = CreateHardwareCommand(async _ =>
        {
            AppendLog("EXIT requested from toolbar");
            logger.Info("用户点击退出软件");
            await ShutdownAsync();
            CloseRequested?.Invoke(this, EventArgs.Empty);
        });

        ToggleLanguageCommand = new RelayCommand(_ => ToggleLanguage());
        ToggleSidebarCommand = new RelayCommand(_ => ToggleSidebar());
        LoginCommand = CreateHardwareCommand(_ => LoginAsync());
        RegisterAccountCommand = CreateHardwareCommand(_ => RegisterAccountAsync());
        ViewAccountListCommand = CreateHardwareCommand(_ => ViewAccountListAsync());
        SwitchAccountCommand = CreateHardwareCommand(_ => SwitchAccountAsync());
        LogoutCommand = CreateHardwareCommand(_ => LogoutAsync());
        EditPatientCommand = CreateHardwareCommand(_ => EditPatientAsync());
        SwitchPatientCommand = CreateHardwareCommand(_ => SwitchPatientAsync());
        CreatePatientCommand = CreateHardwareCommand(_ => CreatePatientAsync());
        EndCurrentSessionCommand = CreateHardwareCommand(_ => EndCurrentSessionAsync());
        NavigateCommand = new AsyncRelayCommand(async (parameter, cancellationToken) =>
        {
            if (parameter is AppPage page)
            {
                await NavigateAsync(page, cancellationToken);
            }
        }, onError: ex =>
        {
            logger.Error("页面切换失败", ex);
            ShellState.FooterStatus = $"页面切换失败：{ex.Message}";
        });

        TiControl.HardwareOperationCompleted += (_, result) => ApplyHardwareResult(result);
        DirectCurrentControl.HardwareOperationCompleted += (_, result) => ApplyHardwareResult(result);
        TiControl.BackRequested += (_, _) => ShowStimulationTypeSelection();
        DirectCurrentControl.BackRequested += (_, _) => ShowStimulationTypeSelection();
        Prescription.UseRequested += (_, item) => ApplyPrescription(item);
        Report.ReuseRequested += (_, item) => ApplyPrescription(item);
        StimulationTypeSelection.TemporalInterferenceRequested += (_, _) => ShowTemporalInterference();
        StimulationTypeSelection.DirectCurrentRequested += (_, _) => ShowDirectCurrent();
        featureVisibilityService.VisibilityChanged += (_, _) => ApplyFeatureVisibility();
        accountService.CurrentUserChanged += (_, _) => NotifyAccountChanged();
        sessionLifecycleCoordinator.CurrentSessionChanged += (_, _) => NotifyUnifiedSessionChanged();
        stimulationStateMachine.StateChanged += (_, _) => NotifyPatientMenuAvailabilityChanged();
        EegSignalCapture.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(EegSignalCaptureViewModel.IsRecording))
            {
                NotifyPatientMenuAvailabilityChanged();
            }
        };
        AssessmentCapture.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(AssessmentCaptureViewModel.ShouldConfirmLeavingWorkbench))
            {
                NotifyPatientMenuAvailabilityChanged();
            }
        };
        hardwareService.ConnectionChanged += HardwareService_ConnectionChanged;
        debugHardwareSimulation.ConnectionChanged += (_, _) => ApplyDebugHardwareSimulationState();
        FemSimulation.Initialize(this);

        // 默认打开电刺激类型选择页。
        Navigation.Select(CurrentPage);
        CurrentPageViewModel = StimulationTypeSelection;

        initializationTask = Task.WhenAll(
            InitializeAccountAsync(),
            InitializePatientAsync(),
            InitializeFeatureVisibilityAsync());
    }

    /// <summary>左侧导航 ViewModel。</summary>
    public NavigationViewModel Navigation { get; }

    /// <summary>顶部语言切换 ViewModel。</summary>
    public LocalizationViewModel Localization { get; }

    /// <summary>患者信息 ViewModel。</summary>
    public PatientViewModel Patient { get; }

    /// <summary>底部状态栏 ViewModel。</summary>
    public ShellStateViewModel ShellState { get; }

    /// <summary>总览监控 ViewModel。</summary>
    public MonitorViewModel Monitor { get; }

    /// <summary>电刺激类型选择页面 ViewModel。</summary>
    public StimulationTypeSelectionViewModel StimulationTypeSelection { get; }

    /// <summary>TI 控制页面 ViewModel。</summary>
    public TiControlViewModel TiControl { get; }

    /// <summary>tDCS 独立页面 ViewModel。</summary>
    public DirectCurrentControlViewModel DirectCurrentControl { get; }

    /// <summary>公用处方管理页面 ViewModel。</summary>
    public PrescriptionViewModel Prescription { get; }

    /// <summary>EEG 采集页面 ViewModel。</summary>
    public EegSignalCaptureViewModel EegSignalCapture { get; }

    /// <summary>采集工作台 ViewModel。</summary>
    public AssessmentCaptureViewModel AssessmentCapture { get; }

    /// <summary>FEM 仿真页面 ViewModel。</summary>
    public FemSimulationViewModel FemSimulation { get; }

    /// <summary>设备管理页面 ViewModel。</summary>
    public DeviceViewModel Device { get; }

    /// <summary>关于页面 ViewModel（同时承载管理员功能显示配置）。</summary>
    public ConfigViewModel Config { get; }

    /// <summary>报告页面 ViewModel。</summary>
    public ReportViewModel Report { get; }

    public AuditTrailViewModel AuditTrail { get; }

    /// <summary>未实现页面的占位 ViewModel。</summary>
    public PlaceholderPageViewModel PlaceholderPage { get; }

    public ICommand ConnectCommand { get; }
    public ICommand DisconnectCommand { get; }
    public ICommand HandshakeCommand { get; }
    public ICommand ReadProductModelCommand { get; }
    public ICommand ReadBoardModelCommand { get; }
    public ICommand CheckImpedanceCommand { get; }
    public ICommand OpenAuditTrailCommand { get; }
    public ICommand ExitCommand { get; }
    public ICommand ToggleLanguageCommand { get; }
    public ICommand ToggleSidebarCommand { get; }
    public ICommand LoginCommand { get; }
    public ICommand RegisterAccountCommand { get; }
    public ICommand ViewAccountListCommand { get; }
    public ICommand SwitchAccountCommand { get; }
    public ICommand LogoutCommand { get; }
    public ICommand EditPatientCommand { get; }
    public ICommand SwitchPatientCommand { get; }
    public ICommand CreatePatientCommand { get; }
    public ICommand EndCurrentSessionCommand { get; }
    public ICommand NavigateCommand { get; }

    public string CurrentSessionSummary => sessionLifecycleCoordinator.CurrentSession is { } session
        ? $"Session：{session.SessionKey}"
        : "Session：未开始";

    public Visibility ActiveSessionVisibility => sessionLifecycleCoordinator.CurrentSession is null
        ? Visibility.Collapsed
        : Visibility.Visible;

    public Visibility SimulationMenuVisibility => featureVisibilityService.IsVisible(FeatureKeys.NavigationFem)
        ? Visibility.Visible
        : Visibility.Collapsed;

    public IToastService Toast => toastService;

    public bool IsLoggedIn => accountService.CurrentUser is not null;

    public bool IsAdminLoggedIn => accountService.IsCurrentUserAdmin();

    public string AccountMenuHeader
    {
        get
        {
            var user = accountService.CurrentUser;
            return user is null ? "登录" : user.LoginName;
        }
    }

    public string CurrentUserSummary
    {
        get
        {
            var user = accountService.CurrentUser;
            return user is null ? "未登录" : $"{user.LoginName} / {user.DisplayName} / ID {user.UserId}";
        }
    }

    public string AccountMenuForeground => IsLoggedIn ? "#5DDA77" : "#E4E8EF";

    public System.Windows.Visibility LoginMenuVisibility => IsLoggedIn ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;

    public System.Windows.Visibility LoggedInMenuVisibility => IsLoggedIn ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

    public System.Windows.Visibility AdminMenuVisibility => IsAdminLoggedIn ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

    public System.Windows.Visibility AuditMenuVisibility => IsLoggedIn ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

    public bool CanManagePatients => accountService.CurrentUser?.RoleId is AccountRoles.Admin or AccountRoles.Doctor;

    public System.Windows.Visibility PatientMenuVisibility => CanManagePatients
        ? System.Windows.Visibility.Visible
        : System.Windows.Visibility.Collapsed;

    public bool IsPatientMenuEnabled => CanManagePatients && !IsPatientOperationLocked;

    private bool IsPatientOperationLocked =>
        sessionLifecycleCoordinator.HasRunningModule || AssessmentCapture.IsActiveForSessionSecurity;

    public bool IsSidebarCollapsed
    {
        get => isSidebarCollapsed;
        private set
        {
            if (SetProperty(ref isSidebarCollapsed, value))
            {
                OnPropertyChanged(nameof(SidebarColumnWidth));
                OnPropertyChanged(nameof(SidebarVisibility));
                OnPropertyChanged(nameof(SidebarToggleGlyph));
                OnPropertyChanged(nameof(SidebarToggleToolTip));
                OnPropertyChanged(nameof(SidebarToggleMargin));
            }
        }
    }

    public GridLength SidebarColumnWidth => IsSidebarCollapsed ? new GridLength(0) : new GridLength(210);

    public Visibility SidebarVisibility => IsSidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;

    public string SidebarToggleGlyph => IsSidebarCollapsed ? "›" : "‹";

    public string SidebarToggleToolTip => IsSidebarCollapsed ? "展开患者栏和导航栏" : "收起患者栏和导航栏";

    public Thickness SidebarToggleMargin => IsSidebarCollapsed ? new Thickness(0, 0, 0, 0) : new Thickness(-11, 0, 0, 0);

    /// <summary>
    /// 关闭窗口请求事件。
    /// View 层订阅后执行真正的窗口关闭。
    /// </summary>
    public event EventHandler? CloseRequested;

    /// <summary>
    /// 当前页面显示的 ViewModel。
    /// 切换 CurrentPage 时会自动切换为对应子 ViewModel。
    /// </summary>
    public ObservableObject? CurrentPageViewModel
    {
        get => currentPageViewModel;
        private set => SetProperty(ref currentPageViewModel, value);
    }

    private ICommand CreateHardwareCommand(Func<object?, Task> execute)
    {
        return new AsyncRelayCommand(
            async (parameter, _) => await execute(parameter),
            onError: ex =>
            {
                logger.Error("界面命令执行失败", ex);
                ShellState.FooterStatus = $"操作失败：{ex.Message}";
            });
    }

    /// <summary>
    /// 当前页面。设置时会自动切换 CurrentPageViewModel 和导航选中状态。
    /// </summary>
    public AppPage CurrentPage
    {
        get => currentPage;
        set => Navigate(value);
    }

    /// <summary>
    /// 采集工作台正在执行正式采集或演示播放时，离开页面会打断当前流程。
    /// 所有页面切换入口都先经过这里确认，避免误点左侧导航造成数据丢失。
    /// </summary>
    private bool ConfirmLeavingCaptureWorkbench(AppPage nextPage)
    {
        if (currentPage != AppPage.AssessmentCapture
            || nextPage == AppPage.AssessmentCapture
            || !AssessmentCapture.ShouldConfirmLeavingWorkbench)
        {
            return true;
        }

        var confirmed = userDialogService.ConfirmWarning(
            AssessmentCapture.WorkbenchLeaveWarningTitle,
            AssessmentCapture.WorkbenchLeaveWarningMessage,
            AssessmentCapture.WorkbenchLeaveConfirmText,
            AssessmentCapture.WorkbenchLeaveCancelText);

        if (confirmed)
        {
            if (AssessmentCapture.IsDemoPlaying)
            {
                AssessmentCapture.CancelDemoPlaybackForNavigation();
            }
            else if (AssessmentCapture.IsQuestionnaireInProgress)
            {
                AssessmentCapture.DiscardActiveQuestionnaireAnswers();
            }

            AppendLog($"CAPTURE leave confirmed from {AssessmentCapture.CurrentModule}");
            return true;
        }

        AppendLog($"CAPTURE leave canceled from {AssessmentCapture.CurrentModule}");
        return false;
    }

    private async Task<bool> ConfirmLeavingEegCaptureAsync(AppPage nextPage, CancellationToken cancellationToken)
    {
        if (currentPage != AppPage.EegSignalCapture
            || nextPage == AppPage.EegSignalCapture
            || !EegSignalCapture.IsRecording)
        {
            return true;
        }

        var confirmed = userDialogService.ConfirmWarning(
            "停止 EEG 采集",
            "当前 EEG 正在采集。切换到其他页面会停止本次采集，并保留当前最后一页波形显示。是否继续切换？",
            "停止并切换",
            "继续采集");

        if (!confirmed)
        {
            AppendLog("EEG leave canceled while recording");
            return false;
        }

        await EegSignalCapture.StopForNavigationAsync(cancellationToken);
        ShellState.FooterStatus = "EEG 采集已停止，已保留最后一页波形";
        AppendLog($"EEG leave confirmed -> {nextPage}");
        return true;
    }

    /// <summary>
    /// 根据页面枚举找到应显示的子 ViewModel。
    /// 已实现页面返回对应 VM；未实现页面返回占位 VM。
    /// </summary>
    private ObservableObject ResolvePageViewModel(AppPage page)
    {
        return page switch
        {
            AppPage.FemSimulation => FemSimulation,
            AppPage.Settings => Config,
            AppPage.Dashboard => Monitor,
            AppPage.Control => StimulationTypeSelection,
            AppPage.EegSignalCapture => EegSignalCapture,
            AppPage.ClosedLoopControl => ConfigurePlaceholder(page),
            AppPage.AssessmentCapture => AssessmentCapture,
            AppPage.ElectrodePlanning => ConfigurePlaceholder(page),
            AppPage.HeadModel => ConfigurePlaceholder(page),
            AppPage.ProtocolManager => Prescription,
            AppPage.TreatmentHistory => Report,
            AppPage.Help => ConfigurePlaceholder(page),
            _ => TiControl
        };
    }

    /// <summary>
    /// 配置未实现页面的占位 VM。
    /// </summary>
    private PlaceholderPageViewModel ConfigurePlaceholder(AppPage page)
    {
        PlaceholderPage.Configure(PageTitle, Localization.PlaceholderDescription(page));
        return PlaceholderPage;
    }

    /// <summary>当前页面标题，取自多语言服务。</summary>
    public string PageTitle => Localization.PageTitle(CurrentPage);

    /// <summary>
    /// 构建左侧导航项。
    /// </summary>
    private void BuildNavigationItems()
    {
        Navigation.SetItems(CreateNavigationItems());
    }

    /// <summary>
    /// 创建导航项枚举。顺序决定左侧导航栏显示顺序。
    /// </summary>
    private IEnumerable<NavItem> CreateNavigationItems()
    {
        foreach (var definition in FeatureCatalog.Navigation)
        {
            if (featureVisibilityService.IsVisible(definition.Key))
            {
                yield return new NavItem(
                    definition.Page,
                    Localization.FeatureText(definition.LocalizationKey));
            }
        }

        // 关于页是恢复导航显示的唯一入口，固定显示且不允许被屏蔽。
        yield return new NavItem(AppPage.Settings, Localization.SettingsText);
    }

    /// <summary>
    /// 语言切换后刷新导航文字，并更新当前占位页文字。
    /// </summary>
    private void RefreshNavigationText()
    {
        foreach (var item in Navigation.Items)
        {
            item.Text = item.Page switch
            {
                AppPage.Dashboard => Localization.PageTitle(AppPage.Dashboard),
                AppPage.Control => Localization.ControlText,
                AppPage.EegSignalCapture => Localization.EegSignalCaptureText,
                AppPage.ClosedLoopControl => Localization.ClosedLoopControlText,
                AppPage.AssessmentCapture => Localization.AssessmentCaptureText,
                AppPage.ElectrodePlanning => Localization.ElectrodePlanningText,
                AppPage.HeadModel => Localization.HeadModelText,
                AppPage.FemSimulation => Localization.FemSimulationText,
                AppPage.ProtocolManager => Localization.ProtocolManagerText,
                AppPage.TreatmentHistory => Localization.TreatmentHistoryText,
                AppPage.Settings => Localization.SettingsText,
                _ => Localization.PageTitle(item.Page)
            };
        }

        if (CurrentPageViewModel == PlaceholderPage)
        {
            PlaceholderPage.Configure(PageTitle, Localization.PlaceholderDescription(CurrentPage));
        }
    }

    /// <summary>
    /// 写入本次运行日志。
    /// Debug 构建会同步输出到独立控制台窗口；Release 构建只写入日志文件。
    /// </summary>
    public void AppendLog(string message)
    {
        logger.Debug(message);
    }

    /// <summary>
    /// 把硬件操作结果应用到界面状态。
    /// </summary>
    private void ApplyHardwareResult(HardwareOperationResult result)
    {
        ShellState.IsDeviceConnected = result.IsConnected;
        ShellState.FooterStatus = result.FooterStatus;
        connectCommand.RaiseCanExecuteChanged();
    }

    private bool CanConnectDevice()
    {
        return !hardwareService.IsConnected
            && !hardwareService.IsConnecting
            && !debugHardwareSimulation.IsConnected;
    }

    private void ApplyDebugHardwareSimulationState()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(ApplyDebugHardwareSimulationState);
            return;
        }

        ShellState.IsDeviceConnected = debugHardwareSimulation.IsConnected || hardwareService.IsConnected;
        if (debugHardwareSimulation.IsConnected)
        {
            ShellState.FooterStatus = "设备：DEBUG 模拟联机 | 不向真实仪器发送命令";
        }

        connectCommand.RaiseCanExecuteChanged();
    }

    private void HardwareService_ConnectionChanged(
        object? sender,
        HardwareConnectionChangedEventArgs entry)
    {
        void ApplyChange()
        {
            ShellState.IsDeviceConnected = entry.IsConnected;
            connectCommand.RaiseCanExecuteChanged();

            if (entry.Reason == HardwareConnectionChangeReason.HeartbeatLost)
            {
                toastService.ShowError("仪器已断联", entry.Message);
            }
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            ApplyChange();
        }
        else
        {
            _ = dispatcher.InvokeAsync(ApplyChange);
        }
    }

    private void HandleImpedanceRefreshError(Exception exception)
    {
        logger.Error("阻抗刷新失败", exception);
        ShellState.FooterStatus = $"阻抗刷新失败：{exception.Message}";
        toastService.ShowError("刷新失败", "未收到有效的阻抗反馈，请检查设备连接后重试。");
    }

    private void HandleDeviceOperationError(string title, string message, Exception exception)
    {
        logger.Error(title, exception);
        ShellState.FooterStatus = $"{title}：{exception.Message}";
        toastService.ShowError(title, $"{message}\n\n实际原因：{exception.Message}");
    }

    /// <summary>导航到指定页面。</summary>
    public void Navigate(AppPage page)
    {
        if (currentPage == page && page == AppPage.Control)
        {
            ShowStimulationTypeSelection();
            return;
        }

        if (EegSignalCapture.IsRecording && currentPage == AppPage.EegSignalCapture && page != currentPage)
        {
            throw new InvalidOperationException("EEG 采集中请使用异步页面切换入口。");
        }

        if (ConfirmLeavingCaptureWorkbench(page))
        {
            ChangeCurrentPage(page);
        }
    }

    public async Task NavigateAsync(AppPage page, CancellationToken cancellationToken = default)
    {
        if (currentPage == page)
        {
            if (page == AppPage.Control)
            {
                ShowStimulationTypeSelection();
            }

            return;
        }

        if (!await ConfirmLeavingEegCaptureAsync(page, cancellationToken) || !ConfirmLeavingCaptureWorkbench(page))
        {
            return;
        }

        ChangeCurrentPage(page);
    }

    private void ChangeCurrentPage(AppPage page)
    {
        if (currentPage == AppPage.Settings && page != AppPage.Settings)
        {
            Config.LeaveSettingsPage();
        }

        if (!SetProperty(ref currentPage, page))
        {
            return;
        }

        if (page == AppPage.Settings)
        {
            Config.EnterSettingsPage();
        }

        CurrentPageViewModel = ResolvePageViewModel(page);
        Navigation.Select(page);
        OnPropertyChanged(nameof(PageTitle));
        AppendLog($"NAVIGATE {page}");
    }

    private void ShowTemporalInterference()
    {
        TiControl.RestoreLastSelection();
        CurrentPageViewModel = TiControl;
        AppendLog("STIMULATION TYPE TI selected");
    }

    private void ShowStimulationTypeSelection()
    {
        StimulationTypeSelection.RefreshVisibility();
        CurrentPageViewModel = StimulationTypeSelection;
        Navigation.Select(AppPage.Control);
        AppendLog("STIMULATION TYPE selection");
    }

    private void ShowDirectCurrent()
    {
        CurrentPageViewModel = DirectCurrentControl;
        ShellState.FooterStatus = "经颅直流电刺激参数设置";
        AppendLog("STIMULATION TYPE tDCS selected");
    }

    private void ApplyPrescription(PrescriptionDefinition prescription)
    {
        if (currentPage != AppPage.Control)
        {
            ChangeCurrentPage(AppPage.Control);
        }

        if (prescription.StimulationType == "TI")
        {
            TiControl.ApplyPrescription(prescription);
            CurrentPageViewModel = TiControl;
            Navigation.Select(AppPage.Control);
        }
        else
        {
            DirectCurrentControl.ApplyPrescription(prescription);
            ShowDirectCurrent();
        }
        ShellState.FooterStatus = $"已应用处方：{prescription.Name}";
        AppendLog($"PRESCRIPTION applied {prescription.Id}");
    }

    private async Task InitializeFeatureVisibilityAsync()
    {
        await Config.InitializeAsync();
        ApplyFeatureVisibility();
    }

    private void ApplyFeatureVisibility()
    {
        BuildNavigationItems();
        OnPropertyChanged(nameof(SimulationMenuVisibility));

        if (!IsPageVisible(CurrentPage))
        {
            var fallbackPage = FeatureCatalog.Navigation
                .Where(item => featureVisibilityService.IsVisible(item.Key))
                .Select(item => (AppPage?)item.Page)
                .FirstOrDefault() ?? AppPage.Settings;
            ChangeCurrentPage(fallbackPage);
            return;
        }

        Navigation.Select(CurrentPage);
        StimulationTypeSelection.RefreshVisibility();
    }

    private bool IsPageVisible(AppPage page)
    {
        if (page == AppPage.Settings)
        {
            return true;
        }

        var definition = FeatureCatalog.Navigation.FirstOrDefault(item => item.Page == page);
        return definition is null || featureVisibilityService.IsVisible(definition.Key);
    }

    public void ToggleSidebar()
    {
        IsSidebarCollapsed = !IsSidebarCollapsed;
    }

    /// <summary>切换语言并刷新相关文字。</summary>
    public void ToggleLanguage()
    {
        Localization.ToggleLanguage();
        RefreshNavigationText();
        OnPropertyChanged(nameof(PageTitle));
        AppendLog($"LANGUAGE {(Localization.IsChinese ? "zh-CN" : "en-US")}");
    }

    public void ConnectDevice()
    {
        ShellState.IsDeviceConnected = true;
        ShellState.FooterStatus = "设备：已联机 | 模型：未加载 | 刺激：空闲";
        logger.Hardware("设备状态：已联机");
    }

    public void DisconnectDevice()
    {
        ShellState.IsDeviceConnected = false;
        ShellState.FooterStatus = "设备：已断开 | 模型：未加载 | 刺激：空闲";
        logger.Hardware("设备状态：已断开");
    }

    private async Task RunDeviceOperationAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default)
    {
        await operation(cancellationToken);
    }

    // 以下空方法是早期调试入口的兼容残留，目前心跳由 HardwareService 统一维护。
    private void StartHeartbeat()
    {
    }

    private async Task StopHeartbeatAsync()
    {
        await Task.CompletedTask;
    }

    private async Task RunHeartbeatLoopAsync(CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
    }

    /// <summary>
    /// 软件退出时调用：先优雅关闭硬件服务，再触发关闭窗口事件。
    /// </summary>
    public async Task ShutdownAsync()
    {
        await shutdownGate.WaitAsync();
        try
        {
            if (shutdownCompleted)
            {
                return;
            }

            await initializationTask;

            if (EegSignalCapture.IsRecording)
            {
                await EegSignalCapture.StopAsync();
            }

            await AssessmentCapture.FlushPendingModuleEventsAsync();

            if (AssessmentCapture.CaptureMediaRecorder.IsRecording)
            {
                AssessmentCapture.CaptureMediaRecorder.RequestStop("interrupted", "软件退出，当前采集已中断。");
            }

            await AssessmentCapture.CaptureMediaRecorder.WaitForIdleAsync();

            await hardwareService.ShutdownAsync();
            await sessionLifecycleCoordinator.InterruptForShutdownAsync();
            shutdownCompleted = true;
        }
        finally
        {
            shutdownGate.Release();
        }
    }

    // 这些方法被对应 Command 调用，再转给 IHardwareService。
    /// <summary>
    /// 登录界面首次显示后读取工作站启动设置；开关启用时只执行一次自动联机。
    /// 失败后不重试、不启动心跳，保持“仪器未联机”并等待用户手动点击联机。
    /// </summary>
    public async Task TryAutomaticConnectionOnceAsync(CancellationToken cancellationToken = default)
    {
        await softwareActivationService.InitializeAsync(cancellationToken);
        if (!softwareActivationService.IsActivated)
        {
            logger.Info("软件尚未激活，跳过启动自动联机");
            return;
        }

        if (Interlocked.Exchange(ref automaticConnectionAttempted, 1) != 0)
        {
            return;
        }

        await startupSettingsService.InitializeAsync(cancellationToken);
        if (!startupSettingsService.AutoConnectOnStartup)
        {
            logger.Info("启动时自动联机已关闭，等待用户手动联机");
            return;
        }

        if (hardwareService.IsConnected || hardwareService.IsConnecting)
        {
            logger.Info("仪器已联机或正在联机，跳过启动自动联机");
            return;
        }

        ShellState.IsDeviceConnected = false;
        connectCommand.RaiseCanExecuteChanged();
        toastService.ShowInformation(
            "正在执行一次自动握手，请稍候……",
            "正在自动联机");
        try
        {
            var result = await hardwareService.ConnectAsync(cancellationToken);
            ApplyHardwareResult(result);
            toastService.ShowSuccess(
                "自动联机成功",
                result.UserMessage ?? "仪器已返回有效握手反馈，心跳检测已启动。");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            ShellState.IsDeviceConnected = false;
            connectCommand.RaiseCanExecuteChanged();
            logger.Warning($"启动自动联机失败，本次不再自动重试：{exception.Message}");
            toastService.ShowError(
                "自动联机失败",
                $"仪器未联机，本次不会自动重试；请检查设备后点击“联机”。\n\n实际原因：{exception.Message}");
        }
    }

    public async Task ConnectDeviceAsync(CancellationToken cancellationToken = default)
    {
        var result = await hardwareService.ConnectAsync(cancellationToken);
        ApplyHardwareResult(result);
        toastService.ShowSuccess("联机成功", result.UserMessage ?? "仪器已返回有效握手反馈，通讯链路已建立。");
    }

    public async Task HandshakeDeviceAsync(CancellationToken cancellationToken = default)
    {
        var result = await hardwareService.HandshakeAsync(cancellationToken);
        ApplyHardwareResult(result);
        toastService.ShowSuccess("握手检测成功", result.UserMessage ?? "已收到仪器的有效握手反馈。");
    }

    public async Task DisconnectDeviceAsync(CancellationToken cancellationToken = default)
    {
        var result = await hardwareService.DisconnectAsync(cancellationToken);
        ApplyHardwareResult(result);
        toastService.ShowSuccess("断开成功", "设备连接已断开。");
    }

    public async Task ReadProductModelAsync()
    {
        ApplyHardwareResult(await hardwareService.ReadProductModelAsync());
    }

    public async Task ReadBoardModelAsync()
    {
        ApplyHardwareResult(await hardwareService.ReadBoardModelAsync());
    }

    public async Task CheckImpedanceAsync(CancellationToken cancellationToken = default)
    {
        var result = await hardwareService.CheckImpedanceAsync(cancellationToken);
        ApplyHardwareResult(result);
        toastService.ShowSuccess("刷新成功", "阻抗值已刷新。");
    }
}
