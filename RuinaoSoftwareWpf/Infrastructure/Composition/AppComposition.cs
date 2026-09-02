namespace RuinaoSoftwareWpf;

using Microsoft.Extensions.DependencyInjection;
using RuinaoSoftwareWpf.ApplicationContracts;
using RuinaoTesHardware;

/// <summary>
/// 依赖注入（DI）容器配置中心。
///
/// 什么是依赖注入：
/// 程序里的日志、硬件通信、本地化等服务由 DI 容器统一创建和分配。
/// 上层代码按接口获取依赖，减少直接 new 具体实现带来的耦合。
/// 这样后续替换实现和编写测试都会更容易。
/// </summary>
public static class AppComposition
{
    // Lazy 保证根容器只创建一次，并保持线程安全。
    private static readonly Lazy<ServiceProvider> RootProvider = new(BuildServiceProvider);
    private static int disposeState;

    /// <summary>
    /// 仅供测试验证组合结果。生产代码通过下方启动边界方法进入对象图。
    /// </summary>
    internal static IServiceProvider Services => RootProvider.Value;

    public static bool IsDisposed => Volatile.Read(ref disposeState) == 2;

    /// <summary>
    /// 获取应用启动日志服务。App 由 WPF 框架创建，不能使用构造函数注入。
    /// </summary>
    public static ILoggingService GetLoggingService() =>
        Services.GetRequiredService<ILoggingService>();

    /// <summary>
    /// 创建由 DI 管理的主窗口。MainWindow 本身只使用构造函数注入。
    /// </summary>
    public static MainWindow CreateMainWindow() =>
        Services.GetRequiredService<MainWindow>();

    /// <summary>
    /// 按 DI 容器管理的逆序释放全部 Singleton 和后台资源。
    /// 正常关闭流程必须在窗口真正关闭前等待该方法完成。
    /// </summary>
    public static async ValueTask DisposeAsync()
    {
        if (!RootProvider.IsValueCreated
            || Interlocked.CompareExchange(ref disposeState, 1, 0) != 0)
        {
            return;
        }

        try
        {
            await RootProvider.Value.DisposeAsync();
            Volatile.Write(ref disposeState, 2);
        }
        catch
        {
            // 释放失败时允许应用退出边界再次尝试，不能把部分释放误报为完成。
            Volatile.Write(ref disposeState, 0);
            throw;
        }
    }

    /// <summary>
    /// 注册所有服务与 ViewModel 的对应关系。
    /// Singleton：整个程序生命周期只创建一个实例。
    /// Transient：每次请求都创建新实例。
    /// </summary>
    private static ServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();

        // ---------- 核心服务（Singleton：全局共享） ----------
        services.AddSingleton<ILoggingService, AppLoggingService>();       // 日志服务
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(AuditTrailStorageOptions.CreateDefault());
        services.AddSingleton<AuditTrailService>();
        services.AddSingleton<IAuditTrailService>(provider => provider.GetRequiredService<AuditTrailService>());
        services.AddSingleton<IAuditTrailStore>(provider => provider.GetRequiredService<AuditTrailService>());
        services.AddSingleton<IToastService, AppToastService>(); // 全局顶部主题 Toast
        services.AddSingleton<IDesktopShortcutService, DesktopShortcutService>(); // 手动创建或更新桌面快捷方式
        services.AddSingleton<IRuntimeTelemetryService, RuntimeTelemetryService>(); // CPU、内存、队列与写入延迟遥测
        services.AddSingleton<IRunConfigurationSnapshotService, RunConfigurationSnapshotService>(); // 运行参数不可变快照
        services.AddSingleton<IAppDatabaseInitializer, AppDatabaseInitializer>(); // EF Core 数据库迁移入口
        services.AddSingleton<IAppDatabaseWriteCoordinator, AppDatabaseWriteCoordinator>(); // 运行期数据库按库串行写入协调器
        services.AddSingleton<PatientDataProtector>(); // 患者敏感字段自动密钥加密
        services.AddSingleton<IReleaseIntegrityVerifier, ReleaseIntegrityVerifier>();
        services.AddSingleton<IReleaseIntegrityStateStore, SqliteReleaseIntegrityStateStore>();
        services.AddSingleton<IIntegrityCheckService, IntegrityCheckService>();
        services.AddSingleton<IBackupRestoreService, BackupRestoreService>();
        services.AddSingleton<ILocalizationService, AppLocalizationService>(); // 多语言服务
        services.AddSingleton<ITiGroupFactory, DemoTiGroupFactory>();     // TI 刺激组工厂
        services.AddSingleton<IDebugHardwareSimulationService, DebugHardwareSimulationService>(); // DEBUG 显式模拟联机状态
        services.AddSingleton<IDebugStimulationImpedanceProvider, DebugStimulationImpedanceProvider>(); // 仅向DEBUG模拟界面提供稳定阻抗
        services.AddSingleton<IUsbBackplaneDiscovery, WindowsUsbBackplaneDiscovery>();
        services.AddSingleton<IBackplaneTransport, UsbTestCompatibleBackplaneTransport>();
        services.AddSingleton<BackplaneClient>();                         // 真实libusbK链路与V1.4应答匹配
        services.AddSingleton<TesHardwareDeviceClient>();                // 共用硬件 DLL 的业务 API
        services.AddSingleton<RuinaoTesHardwareBridge>();                // WPF 只做应用模型与 DLL API 适配
        services.AddSingleton<IAuditLogService, AuditLogService>();       // 兼容状态机同步接口，底层写入独立安全审计库
        services.AddSingleton<IDeviceStateMachine, DeviceStateMachine>(); // 设备状态机
        services.AddSingleton<IStimulationStateMachine, StimulationStateMachine>(); // 刺激状态机
        services.AddSingleton<IHeadModelStateMachine, HeadModelStateMachine>(); // 头模型状态机
        services.AddSingleton<ISafetyService, SafetyService>();           // 安全监控服务
        services.AddSingleton<IHardwareService, HardwareService>();       // 硬件业务服务
        services.AddSingleton<IHardwareConnectionState>(
            provider => provider.GetRequiredService<IHardwareService>());
        services.AddSingleton<IStimulationDeviceGateway, StimulationDeviceGateway>();
        services.AddSingleton<IStimulationEngine, StimulationEngine>();   // 刺激控制引擎
        services.AddSingleton<ITiWaveformPreviewFactory, TiWaveformPreviewFactory>(); // TI业务到共享正弦计划的隔离映射
        services.AddSingleton<ITacsWaveformPreviewFactory, TacsWaveformPreviewFactory>(); // 独立tACS业务到共享正弦计划的映射
        services.AddSingleton<IPrescriptionService, PrescriptionService>(); // 处方服务
        services.AddSingleton<SqliteCaptureRecordingRepository>(); // 采集工作台本地记录仓储
        services.AddSingleton<ICaptureRecordingRepository>(provider => provider.GetRequiredService<SqliteCaptureRecordingRepository>());
        services.AddSingleton<IEegRecordingRepository>(provider => provider.GetRequiredService<SqliteCaptureRecordingRepository>());
        services.AddSingleton<IUnifiedSessionRepository>(provider => provider.GetRequiredService<SqliteCaptureRecordingRepository>());
        services.AddSingleton<IAssessmentRunStore>(provider => provider.GetRequiredService<SqliteCaptureRecordingRepository>());
        services.AddSingleton<IAssessmentRunCoordinator, AssessmentRunCoordinator>();
        services.AddSingleton<ApplicationContracts.IAssessmentModule, AssessmentModuleLifecycleService>();
        services.AddSingleton<IUnifiedSessionService, UnifiedSessionService>(); // 电刺激、EEG、数字表型共享 Session 与时间轴
        services.AddSingleton<CaptureMediaRecorder>(); // OpenCV、音频、编码和仓储的底层录制实现
        services.AddSingleton<ICaptureMediaBackend>(
            provider => provider.GetRequiredService<CaptureMediaRecorder>());
        services.AddSingleton<ICaptureVideoFrameSink>(
            provider => provider.GetRequiredService<CaptureMediaRecorder>());
        services.AddSingleton<ICaptureFormRecordService>(
            provider => provider.GetRequiredService<CaptureMediaRecorder>());
        services.AddSingleton<ICaptureMediaService, CaptureMediaService>(); // 生产调用方统一使用的纯应用层媒体控制入口
        services.AddSingleton<ICaptureVideoFrameWriter, CaptureVideoFrameWriter>();
        services.AddSingleton<ICaptureAudioRecorder, CaptureAudioRecorder>();
        services.AddSingleton<ICaptureMediaEncoder, CaptureMediaEncoder>();
        services.AddSingleton<ICaptureMediaSyncProbe, CaptureMediaSyncProbe>();
        services.AddSingleton<IModuleEventRecorder, ModuleEventRecorder>(); // 模块事件顺序写入与退出等待
        services.AddSingleton<ICameraFaceAnalyzer, OpenVinoCameraFaceAnalyzer>();
        services.AddSingleton<ICameraCaptureProfileStore, JsonCameraCaptureProfileStore>();
        services.AddSingleton<ICameraRecordingQualitySettingsService, LocalCameraRecordingQualitySettingsService>();
        // 单台工作站同一时间只允许一个摄像头会话；采集 ViewModel 也是 Singleton，
        // 因此摄像头服务使用相同生命周期，并由根容器统一释放。
        services.AddSingleton<ICameraCaptureService, OpenCvCameraCaptureService>();
        services.AddSingleton<IUserDialogService, UserDialogService>(); // 统一确认弹窗服务
        services.AddSingleton<IDeviceTopologyDialogService, DeviceTopologyDialogService>(); // DEBUG设备拓扑弹窗边界
        services.AddSingleton<IStimulationImpedanceDiagnosticDialogService, StimulationImpedanceDiagnosticDialogService>(); // DEBUG阻抗诊断弹窗边界
        services.AddSingleton<IAccountService, LocalAccountService>(); // 本地离线账号服务
        services.AddSingleton<ISoftwareActivationService, SoftwareActivationService>(); // 首次运行离线激活与受保护凭据
        services.AddSingleton<IAuthorizationService, AuthorizationService>(); // 登录状态和少量受限业务权限统一校验
        services.AddSingleton<IAuditTrailAdministrationService, AuditTrailAdministrationService>();
        services.AddSingleton<IFeatureVisibilityService, LocalFeatureVisibilityService>(); // Admin 功能显示配置
        services.AddSingleton<IStartupSettingsService, LocalStartupSettingsService>(); // 工作站级启动设置
        services.AddSingleton<IPatientService, LocalPatientService>(); // 本地患者服务
        services.AddSingleton<IExternalFollowUpService, ExternalFollowUpService>(); // 网新测试环境患者查询接口
        services.AddSingleton<IStimulationRecordService, LocalStimulationRecordService>(); // 刺激记录服务
        services.AddSingleton<IEegSegmentFileWriter, EegSegmentFileWriter>(); // EEG 分段二进制写入
        services.AddSingleton<IEegWritePipeline, BoundedEegWritePipeline>(); // EEG 有界生产者/消费者管线
        services.AddSingleton<IEegRecordingService, EegRecordingService>(); // EEG 采集存储服务
        services.AddSingleton<MockEegAcquisitionService>();
        services.AddSingleton<ILegacyEegAcquisitionService>(
            provider => provider.GetRequiredService<MockEegAcquisitionService>());
        services.AddSingleton<ApplicationContracts.IEegAcquisitionService, LegacyEegAcquisitionServiceAdapter>();
        services.AddSingleton<ISessionLifecycleCoordinator, SessionLifecycleCoordinator>(); // Session 收尾和切换患者策略
        services.AddSingleton<IAssessmentActivityState>(provider => provider.GetRequiredService<AssessmentCaptureViewModel>());
        services.AddSingleton<ISessionSecurityService, SessionSecurityService>(); // 无操作锁定、当前账号再认证和安全配置
        services.AddSingleton<GlobalUserActivityMonitor>(); // WPF 全局键鼠、触控与手写笔活动监听
        services.AddSingleton<IHeadModelDataService, HeadModelDataService>(); // 3D 分层网格、LOD、缓存与后台加载
        services.AddSingleton<IReportReadModelService, SqliteReportReadModelService>(); // 独立 SQLite 报表快照读模型

        services.AddSingleton<ISimulationService, FemWorkerSimulationService>();

        // ---------- 单窗口 UI 状态（Singleton：导航切换时保持同一状态与事件订阅） ----------
        services.AddSingleton<NavigationViewModel>();      // 左侧导航
        services.AddSingleton<LocalizationViewModel>();    // 顶部语言切换及所有页面共享语言状态
        services.AddSingleton<PatientViewModel>();         // 患者信息
        services.AddSingleton<ShellStateViewModel>();      // 底部状态栏
        services.AddSingleton<MonitorViewModel>();         // 总览面板
        services.AddSingleton<StimulationTypeSelectionViewModel>(); // 电刺激类型选择页
        services.AddSingleton<TiControlViewModel>();       // TI 控制面板
        services.AddSingleton<DirectCurrentControlViewModel>(); // tDCS 独立页面
        services.AddSingleton<TacsControlViewModel>(); // tACS 独立页面
        services.AddSingleton<PulseCurrentControlViewModel>(); // tPCS 参数页面
        services.AddSingleton<MonophasicPulseCurrentControlViewModel>(); // M-tPCS 独立页面
        services.AddSingleton<IStimulationModeModule, TemporalInterferenceStimulationModeModule>();
        services.AddSingleton<IStimulationModeModule, DirectCurrentStimulationModeModule>();
        services.AddSingleton<IStimulationModeModule, TacsStimulationModeModule>();
        services.AddSingleton<IStimulationModeModule, PulseCurrentStimulationModeModule>();
        services.AddSingleton<IStimulationModeModule, MonophasicPulseCurrentStimulationModeModule>();
        services.AddSingleton<StimulationModeRegistry>(); // 刺激模式统一注册、页面路由与处方应用边界
        services.AddSingleton<PrescriptionViewModel>(); // 公用处方管理页面
        services.AddSingleton<EegSignalCaptureViewModel>(); // EEG 采集面板
        services.AddSingleton<AssessmentCaptureViewModel>(); // 采集工作台：导航切换时保留模块进度
        services.AddSingleton<AssessmentEntryViewModel>(); // 数字表型采集的患者级 Run 入口
        services.AddSingleton<AssessmentPatientMatchingViewModel>(); // 外部患者匹配页面
        services.AddSingleton<AssessmentFeatureHostViewModel>(); // 入口与采集工作台的单一页面宿主
        services.AddSingleton<AssessmentWorkbenchCoordinator>(); // 数字表型工作台流程协调器和模块 VM 容器
        services.AddSingleton<FemSimulationViewModel>();   // FEM 仿真面板
        services.AddSingleton<DeviceViewModel>();          // 设备管理面板
        services.AddSingleton<DeviceTopologyDialogViewModel>(); // DEBUG设备拓扑快照与手动刷新
        services.AddSingleton<StimulationImpedanceDiagnosticDialogViewModel>(); // DEBUG阻抗原始值与映射诊断
        services.AddSingleton<ConfigViewModel>();          // 设置面板
        services.AddSingleton<SessionLockViewModel>();     // 应用会话锁屏
        services.AddSingleton<AuditTrailViewModel>();      // Admin安全审计查询与导出
        services.AddSingleton<ReportViewModel>();          // 报告面板
        services.AddSingleton<PlaceholderPageViewModel>(); // 未实现页面占位
        services.AddSingleton<MainViewModel>();            // 单窗口主界面，聚合以上共享 VM
        services.AddSingleton<MainWindow>();               // WPF 主窗口，通过构造函数接收完整依赖

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
    }
}
