namespace RuinaoTesHardware;

/// <summary>
/// 产品层可直接传给共享硬件DLL的单通道M-tPCS参数。
/// 波形固定为正向单相对称三角脉冲，不提供模式、极性和单次时长参数。
/// </summary>
public sealed record MonophasicPulseCurrentStimulationParameters(
    byte BoardAddress,
    int Channel,
    decimal CurrentMilliampere,
    decimal RampUpDownSeconds,
    decimal IntervalSeconds,
    decimal TotalDurationSeconds);

/// <summary>
/// M-tPCS产品参数转换后的确定性硬件计划。
/// </summary>
public sealed record MonophasicPulseCurrentStimulationPlan(
    MonophasicPulseCurrentStimulationParameters Parameters,
    decimal SinglePulseDurationSeconds,
    decimal CycleDurationSeconds,
    int PlannedPulseCount,
    decimal ScheduledWaveformDurationSeconds,
    decimal ZeroOutputTailSeconds,
    uint EnableMask,
    uint ConfigurationVersion,
    uint WaveformType,
    uint DurationMicroseconds,
    int LowDa,
    int HighDa,
    uint RiseMicroseconds,
    uint HighHoldMicroseconds,
    uint FallMicroseconds,
    uint LowHoldMicroseconds,
    uint TotalTimeMilliseconds);
