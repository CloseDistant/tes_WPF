namespace RuinaoSoftwareWpf;

/// <summary>应用层交给设备适配器的单通道 tDCS 参数；不暴露协议帧。</summary>
internal sealed record DirectCurrentHardwareParameters(
    byte BoardAddress,
    int PhysicalChannelNumber,
    decimal CurrentMilliampere,
    decimal RampUpSeconds,
    decimal RampDownSeconds,
    decimal TotalDurationSeconds,
    bool IsContinuous,
    decimal IntervalSeconds,
    decimal SingleDurationSeconds,
    bool ReversePolarity);
