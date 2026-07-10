using System.Windows.Input;
using System.Windows;
using System.Windows.Controls;
using RuinaoHardwareDebugWpf.Views.Dialogs;

namespace RuinaoHardwareDebugWpf;

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
    private readonly IPatientService patientService;
    private readonly ISessionLifecycleCoordinator sessionLifecycleCoordinator;

    private AppPage currentPage = AppPage.Control;
    private ObservableObject? currentPageViewModel;
    private bool isSidebarCollapsed;
    private readonly SemaphoreSlim shutdownGate = new(1, 1);
    private bool shutdownCompleted;
    private Task initializationTask = Task.CompletedTask;

    /// <summary>
    /// 构造函数由 DI 容器注入所需服务和子 ViewModel。
    /// </summary>
    public MainViewModel(
        IHardwareService hardwareService,
        ILoggingService logger,
        IUserDialogService userDialogService,
        IAccountService accountService,
        IPatientService patientService,
        ISessionLifecycleCoordinator sessionLifecycleCoordinator,
        NavigationViewModel navigation,
        LocalizationViewModel localization,
        PatientViewModel patient,
        ShellStateViewModel shellState,
        MonitorViewModel monitor,
        TiControlViewModel tiControl,
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
        this.patientService = patientService;
        this.sessionLifecycleCoordinator = sessionLifecycleCoordinator;

        Navigation = navigation;
        Localization = localization;
        Patient = patient;
        ShellState = shellState;
        Monitor = monitor;
        TiControl = tiControl;
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

        // 硬件命令统一通过 CreateHardwareCommand 包装异常处理。
        ConnectCommand = CreateHardwareCommand(_ => ConnectDeviceAsync());
        DisconnectCommand = CreateHardwareCommand(_ => DisconnectDeviceAsync());
        HandshakeCommand = CreateHardwareCommand(_ => HandshakeDeviceAsync());
        ReadProductModelCommand = CreateHardwareCommand(_ => ReadProductModelAsync());
        ReadBoardModelCommand = CreateHardwareCommand(_ => ReadBoardModelAsync());
        CheckImpedanceCommand = CreateHardwareCommand(_ => CheckImpedanceAsync());

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
        accountService.CurrentUserChanged += (_, _) => NotifyAccountChanged();
        sessionLifecycleCoordinator.CurrentSessionChanged += (_, _) => NotifyUnifiedSessionChanged();
        FemSimulation.Initialize(this);

        // 默认打开 TI 控制页面。
        Navigation.Select(CurrentPage);
        CurrentPageViewModel = TiControl;

        initializationTask = Task.WhenAll(InitializeAccountAsync(), InitializePatientAsync());
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

    /// <summary>TI 控制页面 ViewModel。</summary>
    public TiControlViewModel TiControl { get; }

    /// <summary>EEG 采集页面 ViewModel。</summary>
    public EegSignalCaptureViewModel EegSignalCapture { get; }

    /// <summary>采集工作台 ViewModel。</summary>
    public AssessmentCaptureViewModel AssessmentCapture { get; }

    /// <summary>FEM 仿真页面 ViewModel。</summary>
    public FemSimulationViewModel FemSimulation { get; }

    /// <summary>设备管理页面 ViewModel。</summary>
    public DeviceViewModel Device { get; }

    /// <summary>设置页面 ViewModel。</summary>
    public ConfigViewModel Config { get; }

    /// <summary>报告页面 ViewModel。</summary>
    public ReportViewModel Report { get; }

    /// <summary>未实现页面的占位 ViewModel。</summary>
    public PlaceholderPageViewModel PlaceholderPage { get; }

    public ICommand ConnectCommand { get; }
    public ICommand DisconnectCommand { get; }
    public ICommand HandshakeCommand { get; }
    public ICommand ReadProductModelCommand { get; }
    public ICommand ReadBoardModelCommand { get; }
    public ICommand CheckImpedanceCommand { get; }
    public ICommand ExitCommand { get; }
    public ICommand ToggleLanguageCommand { get; }
    public ICommand ToggleSidebarCommand { get; }
    public ICommand LoginCommand { get; }
    public ICommand RegisterAccountCommand { get; }
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
            AppPage.Control => TiControl,
            AppPage.EegSignalCapture => EegSignalCapture,
            AppPage.ClosedLoopControl => ConfigurePlaceholder(page),
            AppPage.AssessmentCapture => AssessmentCapture,
            AppPage.ElectrodePlanning => ConfigurePlaceholder(page),
            AppPage.HeadModel => ConfigurePlaceholder(page),
            AppPage.ProtocolManager => ConfigurePlaceholder(page),
            AppPage.TreatmentHistory => ConfigurePlaceholder(page),
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
        yield return new NavItem(AppPage.Dashboard, Localization.PageTitle(AppPage.Dashboard));
        yield return new NavItem(AppPage.Control, Localization.ControlText);
        yield return new NavItem(AppPage.EegSignalCapture, Localization.EegSignalCaptureText);
        yield return new NavItem(AppPage.AssessmentCapture, Localization.AssessmentCaptureText);
        yield return new NavItem(AppPage.ClosedLoopControl, Localization.ClosedLoopControlText);
        yield return new NavItem(AppPage.HeadModel, Localization.HeadModelText);
        yield return new NavItem(AppPage.FemSimulation, Localization.FemSimulationText);
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
    }

    /// <summary>导航到指定页面。</summary>
    public void Navigate(AppPage page)
    {
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
        if (!SetProperty(ref currentPage, page))
        {
            return;
        }

        if (page == AppPage.Control)
        {
            TiControl.RestoreLastSelection();
        }

        CurrentPageViewModel = ResolvePageViewModel(page);
        Navigation.Select(page);
        OnPropertyChanged(nameof(PageTitle));
        AppendLog($"NAVIGATE {page}");
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
    public async Task ConnectDeviceAsync()
    {
        ApplyHardwareResult(await hardwareService.ConnectAsync());
    }

    public async Task HandshakeDeviceAsync()
    {
        ApplyHardwareResult(await hardwareService.HandshakeAsync());
    }

    public async Task DisconnectDeviceAsync()
    {
        ApplyHardwareResult(await hardwareService.DisconnectAsync());
    }

    public async Task ReadProductModelAsync()
    {
        ApplyHardwareResult(await hardwareService.ReadProductModelAsync());
    }

    public async Task ReadBoardModelAsync()
    {
        ApplyHardwareResult(await hardwareService.ReadBoardModelAsync());
    }

    public async Task CheckImpedanceAsync()
    {
        ApplyHardwareResult(await hardwareService.CheckImpedanceAsync());
    }
}
