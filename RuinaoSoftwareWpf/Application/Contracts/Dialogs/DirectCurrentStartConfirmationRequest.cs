namespace RuinaoSoftwareWpf;

/// <summary>
/// 单通道 tDCS 启动确认所需的只读参数快照。
/// 数值来自完成业务校验后的刺激参数，确保弹窗展示内容与后续硬件下发内容一致。
/// </summary>
public sealed record DirectCurrentStartConfirmationRequest(
    string ChannelName,
    double CurrentMilliampere,
    bool IsContinuousMode,
    bool IsReversePolarity,
    double RampUpSeconds,
    double RampDownSeconds,
    double TotalDurationSeconds,
    double? SingleDurationSeconds,
    double? IntervalSeconds,
    decimal ImpedanceOhms,
    bool IsImpedanceWarning);
