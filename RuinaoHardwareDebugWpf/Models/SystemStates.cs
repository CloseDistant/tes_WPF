namespace RuinaoHardwareDebugWpf;

/// <summary>
/// 设备连接状态机状态。
/// </summary>
public enum DeviceConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Error
}

/// <summary>
/// 刺激执行状态机状态。
/// </summary>
public enum StimulationExecutionState
{
    Idle,
    Armed,
    Running,
    Paused,
    Completed,
    EmergencyStopped,
    Error
}

/// <summary>
/// 头模型/FEM 流程状态机状态。
/// </summary>
public enum HeadModelState
{
    NoModel,
    ModelLoaded,
    MeshGenerated,
    MontageReady,
    SimulationDone,
    Error
}

/// <summary>
/// 安全规则评估后的建议动作。
/// </summary>
public enum SafetyAction
{
    None,
    Warning,
    Stop,
    EmergencyStop
}

/// <summary>
/// 通用状态转换记录，用于审计日志。
/// </summary>
public sealed record StateTransition<TState>(
    TState From,
    TState To,
    string Trigger,
    DateTimeOffset Timestamp,
    string OperatorId);

/// <summary>
/// 一次传感器采样快照，后续可承载温度、阻抗、电流等监控数据。
/// </summary>
public sealed record SensorSnapshot(
    IReadOnlyList<double> TemperaturesC,
    IReadOnlyList<double> ImpedancesOhm,
    DateTimeOffset Timestamp);

/// <summary>
/// 安全规则评估结果。
/// </summary>
public sealed record SafetyEvaluationResult(
    SafetyAction Action,
    string Reason,
    DateTimeOffset Timestamp);

/// <summary>
/// 单通道阻抗测量结果。
/// </summary>
public sealed record ImpedanceMeasurement(
    byte Channel,
    double ImpedanceOhm,
    DateTimeOffset Timestamp);

/// <summary>
/// 处方列表中使用的轻量摘要。
/// </summary>
public sealed record PrescriptionSummary(
    string Id,
    string Name,
    string Mode,
    bool IsBuiltin);
