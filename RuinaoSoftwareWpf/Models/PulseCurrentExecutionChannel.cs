namespace RuinaoSoftwareWpf;

/// <summary>
/// tPCS 启动时使用的不可变通道参数快照。
/// ViewModel 在进入刺激链路前完成输入校验，硬件层不持有可变的界面模型。
/// </summary>
public sealed record PulseCurrentExecutionChannel(
    int LogicalChannelNumber,
    PulseCurrentParameters Parameters);
