namespace RuinaoSoftwareWpf;

/// <summary>应用层交给共享硬件 DLL 的单通道 M-tPCS 产品参数。</summary>
internal sealed record MonophasicPulseCurrentHardwareParameters(
    byte BoardAddress,
    int PhysicalChannelNumber,
    decimal CurrentMilliampere,
    decimal RampUpDownSeconds,
    decimal IntervalSeconds,
    decimal TotalDurationSeconds);
