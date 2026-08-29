namespace RuinaoTesHardware;

/// <summary>
/// 产品层传给共享硬件DLL的单通道tACS参数。
/// 电流使用mA单边峰值，频率使用整数Hz，时间统一使用秒。
/// </summary>
public sealed record AlternatingCurrentStimulationParameters(
    byte BoardAddress,
    int Channel,
    decimal PeakCurrentMilliampere,
    decimal RampUpSeconds,
    decimal RampDownSeconds,
    uint FrequencyHz,
    decimal TotalDurationSeconds);

public enum AlternatingCurrentWaveformStage
{
    RampUp,
    Stable,
    RampDown,
}

/// <summary>tACS严格等时阶梯方案中的一段正弦波硬件预览。</summary>
public sealed record AlternatingCurrentWaveformSegmentPlan(
    int Index,
    AlternatingCurrentWaveformStage Stage,
    uint StartMicroseconds,
    uint DurationMicroseconds,
    decimal EnvelopeCoefficient,
    decimal PeakCurrentMilliampere,
    uint FrequencyHz,
    uint AmplitudeDa,
    uint PhaseDegree);

/// <summary>tACS产品参数转换后的只读计划，最多包含5段正弦波。</summary>
public sealed record AlternatingCurrentStimulationPlan(
    AlternatingCurrentStimulationParameters Parameters,
    uint EnableMask,
    uint ConfigurationVersion,
    uint TotalTimeMilliseconds,
    IReadOnlyList<AlternatingCurrentWaveformSegmentPlan> Segments);

public sealed record AlternatingCurrentStimulationConfigurationResult(
    AlternatingCurrentStimulationPlan Plan,
    IReadOnlyList<StimulationHardwareCommandResult> WaveformCommands,
    StimulationHardwareCommandResult ControlCommand);

public sealed record AlternatingCurrentStimulationProgress(
    decimal SimulatedCurrentMilliampere,
    decimal EnvelopePeakMilliampere,
    int SegmentIndex,
    AlternatingCurrentWaveformStage? Stage,
    TimeSpan Remaining,
    bool IsCompleted);
