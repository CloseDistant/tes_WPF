namespace RuinaoTesHardware;

public enum DirectCurrentDeliveryMode
{
    Continuous,
    Intermittent,
}

public enum DirectCurrentPolarity
{
    Normal,
    Reversed,
}

/// <summary>
/// 产品层可直接传给硬件DLL的单通道tDCS参数。时间统一为秒，电流统一为mA。
/// 上位机不需要了解类型8、寄存器地址、微秒换算或DA补码。
/// </summary>
public sealed record DirectCurrentStimulationParameters(
    byte BoardAddress,
    int Channel,
    decimal CurrentMilliampere,
    decimal RampUpSeconds,
    decimal RampDownSeconds,
    decimal TotalDurationSeconds,
    DirectCurrentDeliveryMode DeliveryMode,
    decimal IntervalSeconds,
    decimal SingleDurationSeconds,
    DirectCurrentPolarity Polarity);

/// <summary>
/// DLL完成产品参数转换后的只读预览，供工程师工具和诊断日志核对。
/// 正式软件可以忽略本对象中的硬件换算字段。
/// </summary>
public sealed record DirectCurrentStimulationPlan(
    DirectCurrentStimulationParameters Parameters,
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

public enum DirectCurrentConfirmationLevel
{
    DeviceAccepted,
    StateVerified,
}

public enum DirectCurrentDiagnosticCode
{
    ResponseTimeout,
    CommunicationFailure,
}

public sealed class DirectCurrentStimulationException : Exception
{
    public DirectCurrentDiagnosticCode DiagnosticCode { get; }
    public string Operation { get; }

    public DirectCurrentStimulationException(
        DirectCurrentDiagnosticCode diagnosticCode,
        string operation,
        string message,
        Exception innerException)
        : base(message, innerException)
    {
        DiagnosticCode = diagnosticCode;
        Operation = operation;
    }
}

/// <summary>
/// DLL对一次硬件回复整理后的产品级结果，不向上位机暴露协议寄存器模型。
/// </summary>
public sealed record DirectCurrentCommandResult(
    ushort RequestSequence,
    TimeSpan Elapsed,
    DirectCurrentConfirmationLevel ConfirmationLevel,
    uint? HardwareStatusCode,
    string Message);

public sealed record DirectCurrentConfigurationResult(
    DirectCurrentStimulationPlan Plan,
    DirectCurrentCommandResult WaveformCommand,
    DirectCurrentCommandResult ControlCommand);
