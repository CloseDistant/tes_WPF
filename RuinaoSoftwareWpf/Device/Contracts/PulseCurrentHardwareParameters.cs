namespace RuinaoSoftwareWpf;

/// <summary>应用层交给共享硬件 DLL 的单通道 tPCS 产品参数。</summary>
internal sealed record PulseCurrentHardwareParameters(
    byte BoardAddress,
    int PhysicalChannelNumber,
    decimal CurrentMilliampere,
    decimal RampWidthMilliseconds,
    decimal PulseWidthMilliseconds,
    decimal IntervalWidthMilliseconds,
    decimal TreatmentDurationSeconds,
    bool ReversePolarity);
