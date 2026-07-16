namespace RuinaoSoftwareWpf;

/// <summary>
/// 日志服务的具体实现。
///
/// DebugLog 是底层文件写入工具，只允许在本实现内部使用。
/// 上层业务代码统一依赖 ILoggingService，避免静态日志入口散落在各模块。
/// </summary>
public sealed class AppLoggingService : ILoggingService
{
    public string CurrentLogPath => DebugLog.CurrentLogPath;

    public void Debug(string message) => DebugLog.WriteLine(message);

    public void Info(string message) => DebugLog.WriteInfo(message);

    public void Warning(string message) => DebugLog.WriteWarning(message);

    public void Error(string message, Exception? exception = null) => DebugLog.WriteError(message, exception);

    public void Hardware(string message) => DebugLog.WriteHardwareCommunication(message);

    public void HardwareTx(string command, byte[] frame) => DebugLog.WriteHardwareTx(command, frame);

    public void HardwareDecision(string message) => DebugLog.WriteHardwareDecision(message);
}
