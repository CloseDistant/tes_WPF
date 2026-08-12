namespace RuinaoSoftwareWpf;

/// <summary>
/// 本地原型支持的页面枚举。
///
/// 每个值对应左侧导航栏的一个入口，也用于 MainViewModel 切换 CurrentPage。
/// 在算法/FEM/后端未接入前，这些页面先完成跳转和本地状态展示。
/// </summary>
public enum AppPage
{
    /// <summary>总览面板：显示设备状态、温度、阻抗等监控信息。</summary>
    Dashboard,

    /// <summary>TI 控制面板：主要操作区，选择刺激组并下发参数。</summary>
    Control,

    /// <summary>脑电信号采集：预留 EEG 采集、标记同步和采集质量监控入口。</summary>
    EegSignalCapture,

    /// <summary>闭环控制：预留闭环调控、反馈策略和自动干预流程入口。</summary>
    ClosedLoopControl,

    /// <summary>采集工作台：承载面部捕捉、音频和电子问卷等采集流程。</summary>
    AssessmentCapture,

    /// <summary>智能电极规划：后续接入电极布局算法。</summary>
    ElectrodePlanning,

    /// <summary>头模型构建：后续接入 MRI/STL 导入。</summary>
    HeadModel,

    /// <summary>有限元仿真：后续接入 FEM 计算。</summary>
    FemSimulation,

    /// <summary>处方管理：后续接入处方 JSON 管理。</summary>
    ProtocolManager,

    /// <summary>治疗历史：后续接入治疗记录。</summary>
    TreatmentHistory,

    /// <summary>关于页面（同时承载管理员功能显示配置）。</summary>
    Settings,

    /// <summary>帮助页面。</summary>
    Help
}
