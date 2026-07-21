namespace RuinaoSoftwareWpf;

using Microsoft.Extensions.DependencyInjection;
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
    // Lazy 保证 ServiceProvider 只创建一次，并保持线程安全。
    private static readonly Lazy<IServiceProvider> ServiceProvider = new(BuildServiceProvider);

    /// <summary>
    /// 全局唯一的 DI 容器，程序里需要服务的地方可通过它获取实例。
    /// </summary>
    public static IServiceProvider Services => ServiceProvider.Value;

    /// <summary>
    /// 创建主界面 ViewModel，App 启动时调用。
    /// </summary>
    public static MainViewModel CreateMainViewModel()
    {
        return Services.GetRequiredService<MainViewModel>();
    }

    /// <summary>
    /// 注册所有服务与 ViewModel 的对应关系。
    /// Singleton：整个程序生命周期只创建一个实例。
    /// Transient：每次请求都创建新实例。
    /// </summary>
    private static IServiceProvider BuildServiceProvider()
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
        services.AddSingleton<IAppDatabaseWriteCoordinator, AppDatabaseWriteCoordinator>(); // 三个实时模块关键事件全局单写者
        services.AddSingleton<PatientDataProtector>(); // 患者敏感字段自动密钥加密
        services.AddSingleton<IIntegrityCheckService, IntegrityCheckService>();
        services.AddSingleton<IBackupRestoreService, BackupRestoreService>();
        services.AddSingleton<ILocalizationService, AppLocalizationService>(); // 多语言服务
        services.AddSingleton<ITiGroupFactory, DemoTiGroupFactory>();     // TI 刺激组工厂
        services.AddSingleton<IHardwareLink, LogOnlyHardwareTransport>(); // 尚未迁移到V1.4的业务命令暂保留日志链路
        services.AddSingleton<IHardwareTransport, ReliableHardwareTransport>(); // 旧业务命令的命令关联、超时与重试
        services.AddSingleton<IUsbBackplaneDiscovery, WindowsUsbBackplaneDiscovery>();
        services.AddSingleton<IBackplaneTransport, UsbTestCompatibleBackplaneTransport>();
        services.AddSingleton<BackplaneClient>();                         // 真实libusbK链路与V1.4应答匹配
        services.AddSingleton<RuinaoTesProtocolBridge>();                 // UI只能经Bridge调用共用硬件DLL
        services.AddSingleton<IDeviceHal, ProtocolDeviceHal>();           // 硬件抽象层，内部同样指向协议 DLL Bridge
        services.AddSingleton<IAuditLogService, AuditLogService>();       // 兼容状态机同步接口，底层写入独立安全审计库
        services.AddSingleton<IDeviceStateMachine, DeviceStateMachine>(); // 设备状态机
        services.AddSingleton<IStimulationStateMachine, StimulationStateMachine>(); // 刺激状态机
        services.AddSingleton<IHeadModelStateMachine, HeadModelStateMachine>(); // 头模型状态机
        services.AddSingleton<ISafetyService, SafetyService>();           // 安全监控服务
        services.AddSingleton<IHardwareService, HardwareService>();       // 硬件业务服务
        services.AddSingleton<IStimulationEngine, StimulationEngine>();   // 刺激控制引擎
        services.AddSingleton<IPrescriptionService, PrescriptionService>(); // 处方服务
        services.AddSingleton<SqliteCaptureRecordingRepository>(); // 采集工作台本地记录仓储
        services.AddSingleton<ICaptureRecordingRepository>(provider => provider.GetRequiredService<SqliteCaptureRecordingRepository>());
        services.AddSingleton<IEegRecordingRepository>(provider => provider.GetRequiredService<SqliteCaptureRecordingRepository>());
        services.AddSingleton<IUnifiedSessionRepository>(provider => provider.GetRequiredService<SqliteCaptureRecordingRepository>());
        services.AddSingleton<IUnifiedSessionService, UnifiedSessionService>(); // 电刺激、EEG、数字表型共享 Session 与时间轴
        services.AddSingleton<ICaptureMediaRecorder, CaptureMediaRecorder>(); // 采集工作台音视频录制服务
        services.AddSingleton<ICaptureVideoFrameWriter, CaptureVideoFrameWriter>();
        services.AddSingleton<ICaptureAudioRecorder, CaptureAudioRecorder>();
        services.AddSingleton<ICaptureMediaEncoder, CaptureMediaEncoder>();
        services.AddSingleton<ICaptureMediaSyncProbe, CaptureMediaSyncProbe>();
        services.AddSingleton<IModuleEventRecorder, ModuleEventRecorder>(); // 模块事件顺序写入与退出等待
        services.AddTransient<ICameraCaptureService, OpenCvCameraCaptureService>(); // 摄像头设备生命周期
        services.AddSingleton<IUserDialogService, UserDialogService>(); // 统一确认弹窗服务
        services.AddSingleton<IAccountService, LocalAccountService>(); // 本地离线账号服务
        services.AddSingleton<ISoftwareActivationService, SoftwareActivationService>(); // 首次运行离线激活与受保护凭据
        services.AddSingleton<IAuthorizationService, AuthorizationService>(); // 登录状态和少量受限业务权限统一校验
        services.AddSingleton<IAuditTrailAdministrationService, AuditTrailAdministrationService>();
        services.AddSingleton<IFeatureVisibilityService, LocalFeatureVisibilityService>(); // Admin 功能显示配置
        services.AddSingleton<IStartupSettingsService, LocalStartupSettingsService>(); // 工作站级启动设置
        services.AddSingleton<IPatientService, LocalPatientService>(); // 本地患者服务
        services.AddSingleton<IStimulationRecordService, LocalStimulationRecordService>(); // 刺激记录服务
        services.AddSingleton<IEegSegmentFileWriter, EegSegmentFileWriter>(); // EEG 分段二进制写入
        services.AddSingleton<IEegWritePipeline, BoundedEegWritePipeline>(); // EEG 有界生产者/消费者管线
        services.AddSingleton<IEegRecordingService, EegRecordingService>(); // EEG 采集存储服务
        services.AddSingleton<IEegAcquisitionService, MockEegAcquisitionService>(); // EEG 第一阶段：Mock 采集服务
        services.AddSingleton<ISessionLifecycleCoordinator, SessionLifecycleCoordinator>(); // Session 收尾和切换患者策略
        services.AddSingleton<IAssessmentActivityState>(provider => provider.GetRequiredService<AssessmentCaptureViewModel>());
        services.AddSingleton<ISessionSecurityService, SessionSecurityService>(); // 无操作锁定、当前账号再认证和安全配置
        services.AddSingleton<GlobalUserActivityMonitor>(); // WPF 全局键鼠、触控与手写笔活动监听
        services.AddSingleton<IHeadModelDataService, HeadModelDataService>(); // 3D 分层网格、LOD、缓存与后台加载
        services.AddSingleton<IReportReadModelService, SqliteReportReadModelService>(); // 独立 SQLite 报表快照读模型

        // ---------- 占位服务（当前功能未实现，先使用 Null 实现） ----------
        services.AddSingleton<IDataProcessingService, NullDataProcessingService>();
        services.AddSingleton<ISimulationService, FemWorkerSimulationService>();
        services.AddSingleton<IConfigService, NullConfigService>();
        services.AddSingleton<IReportService, NullReportService>();

        // ---------- 子页面/子模块 ViewModel（Transient：每次新建） ----------
        services.AddTransient<NavigationViewModel>();      // 左侧导航
        services.AddTransient<LocalizationViewModel>();    // 顶部语言切换
        services.AddTransient<PatientViewModel>();         // 患者信息
        services.AddTransient<ShellStateViewModel>();      // 底部状态栏
        services.AddTransient<MonitorViewModel>();         // 总览面板
        services.AddTransient<StimulationTypeSelectionViewModel>(); // 电刺激类型选择页
        services.AddTransient<TiControlViewModel>();       // TI 控制面板
        services.AddTransient<DirectCurrentControlViewModel>(); // tDCS 独立页面
        services.AddTransient<PrescriptionViewModel>(); // 公用处方管理页面
        services.AddSingleton<EegSignalCaptureViewModel>(); // EEG 采集面板
        services.AddSingleton<AssessmentCaptureViewModel>(); // 采集工作台：导航切换时保留模块进度
        services.AddSingleton<AssessmentWorkbenchCoordinator>(); // 数字表型工作台流程协调器和模块 VM 容器
        services.AddTransient<FemSimulationViewModel>();   // FEM 仿真面板
        services.AddTransient<DeviceViewModel>();          // 设备管理面板
        services.AddTransient<ConfigViewModel>();          // 设置面板
        services.AddSingleton<SessionLockViewModel>();     // 应用会话锁屏
        services.AddTransient<AuditTrailViewModel>();      // Admin安全审计查询与导出
        services.AddTransient<ReportViewModel>();          // 报告面板
        services.AddTransient<PlaceholderPageViewModel>(); // 未实现页面占位
        services.AddTransient<MainViewModel>();            // 主界面（聚合以上所有 VM）

        return services.BuildServiceProvider();
    }
}
