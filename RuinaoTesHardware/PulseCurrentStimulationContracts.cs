namespace RuinaoTesHardware;

public enum PulseCurrentPolarity
{
    Normal,
    Reversed,
}

/// <summary>
/// 产品层可直接传给共享硬件DLL的单通道tPCS参数。
/// 脉冲时间使用毫秒，治疗时间使用秒，电流使用mA。
/// </summary>
public sealed record PulseCurrentStimulationParameters(
    byte BoardAddress,
    int Channel,
    decimal CurrentMilliampere,
    decimal RampWidthMilliseconds,
    decimal PulseWidthMilliseconds,
    decimal IntervalWidthMilliseconds,
    decimal TreatmentDurationSeconds,
    PulseCurrentPolarity Polarity);

/// <summary>tPCS单段波形的确定性硬件预览。</summary>
public sealed record PulseCurrentWaveformSegmentPlan(
    uint WaveformType,
    uint DurationMicroseconds,
    int LowDa,
    int HighDa,
    uint RiseMicroseconds,
    uint HighHoldMicroseconds,
    uint FallMicroseconds,
    uint LowHoldMicroseconds,
    uint RepeatCount);

/// <summary>
/// tPCS产品参数转换后的只读计划。治疗时间不含首次渐升；硬件总运行时间为渐升时间加治疗时间。
/// </summary>
public sealed record PulseCurrentStimulationPlan(
    PulseCurrentStimulationParameters Parameters,
    decimal SignedCurrentMilliampere,
    int PlannedPulseCount,
    decimal ScheduledPulseDurationMilliseconds,
    decimal ZeroOutputTailMilliseconds,
    uint EnableMask,
    uint ConfigurationVersion,
    PulseCurrentWaveformSegmentPlan InitialRampSegment,
    PulseCurrentWaveformSegmentPlan PulseTrainSegment,
    uint TreatmentDurationMilliseconds,
    uint TotalTimeMilliseconds);

public sealed record PulseCurrentStimulationConfigurationResult(
    PulseCurrentStimulationPlan Plan,
    StimulationHardwareCommandResult InitialRampCommand,
    StimulationHardwareCommandResult PulseTrainCommand,
    StimulationHardwareCommandResult ControlCommand);
