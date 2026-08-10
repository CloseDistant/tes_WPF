namespace RuinaoSoftwareWpf;

/// <summary>
/// 应用层交给设备适配器的单通道tDCS参数。该模型保留产品单位，
/// 不暴露类型8、寄存器地址、DA值或协议帧。
/// </summary>
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

/// <summary>应用层交给共享硬件DLL的单通道 M-tPCS 产品参数。</summary>
internal sealed record MonophasicPulseCurrentHardwareParameters(
    byte BoardAddress,
    int PhysicalChannelNumber,
    decimal CurrentMilliampere,
    decimal RampUpDownSeconds,
    decimal IntervalSeconds,
    decimal TotalDurationSeconds);
