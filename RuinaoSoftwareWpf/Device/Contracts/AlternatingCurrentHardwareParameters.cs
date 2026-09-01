namespace RuinaoSoftwareWpf;

/// <summary>正式软件交给设备适配器的单通道tACS参数；不暴露协议帧或寄存器。</summary>
internal sealed record AlternatingCurrentHardwareParameters(
    byte BoardAddress,
    int PhysicalChannelNumber,
    decimal PeakCurrentMilliampere,
    decimal RampUpSeconds,
    decimal RampDownSeconds,
    uint FrequencyHz,
    decimal TotalDurationSeconds);

internal sealed record AlternatingCurrentHardwareConfigurationProgress(
    int CompletedCommandCount,
    int TotalCommandCount,
    string Stage);
