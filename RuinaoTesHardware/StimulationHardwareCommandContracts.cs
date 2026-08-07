namespace RuinaoTesHardware;

public enum StimulationHardwareConfirmationLevel
{
    DeviceAccepted,
    StateVerified,
}

public enum StimulationHardwareDiagnosticCode
{
    ResponseTimeout,
    CommunicationFailure,
}

public sealed class StimulationHardwareException : Exception
{
    public StimulationHardwareDiagnosticCode DiagnosticCode { get; }
    public string Operation { get; }

    public StimulationHardwareException(
        StimulationHardwareDiagnosticCode diagnosticCode,
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
/// 共享硬件DLL对一次刺激命令回复整理后的产品级结果。
/// </summary>
public sealed record StimulationHardwareCommandResult(
    ushort RequestSequence,
    TimeSpan Elapsed,
    StimulationHardwareConfirmationLevel ConfirmationLevel,
    uint? HardwareStatusCode,
    string Message);

public sealed record StimulationHardwareConfigurationResult<TPlan>(
    TPlan Plan,
    StimulationHardwareCommandResult WaveformCommand,
    StimulationHardwareCommandResult ControlCommand);
