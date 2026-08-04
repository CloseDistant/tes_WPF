namespace RuinaoSoftwareWpf;

/// <summary>单通道 tDCS 启动确认所需的已校验参数快照。</summary>
public sealed record DirectCurrentChannelStartConfirmationRequest(
    string ChannelName,
    double CurrentMilliampere,
    bool IsContinuousMode,
    bool IsReversePolarity,
    double RampUpSeconds,
    double RampDownSeconds,
    double TotalDurationSeconds,
    double SingleDurationSeconds,
    double IntervalSeconds,
    decimal ImpedanceOhms,
    StimulationImpedanceStatus ImpedanceStatus);
