namespace RuinaoHardwareDebugWpf;

/// <summary>
/// 日志服务接口。
///
/// 通过接口抽象日志，方便：
/// - 测试时传入“假日志”，不产生真实日志文件。
/// - 以后想换 NLog/Serilog 等第三方日志库时，只换实现类即可。
/// </summary>
public interface ILoggingService
{
    /// <summary>当前日志文件路径。</summary>
    string CurrentLogPath { get; }

    /// <summary>记录 Debug 级别日志。Debug 构建会显示在控制台。</summary>
    void Debug(string message);

    /// <summary>记录 Info 级别日志。</summary>
    void Info(string message);

    /// <summary>记录 Warning 级别日志。</summary>
    void Warning(string message);

    /// <summary>记录 Error 级别日志。可附带异常对象。</summary>
    void Error(string message, Exception? exception = null);

    /// <summary>记录硬件通信相关日志，会带 [硬件通信] 前缀。</summary>
    void Hardware(string message);

    /// <summary>记录硬件 TX 原始帧。Release 默认过滤，主要用于 Debug 联调。</summary>
    void HardwareTx(string command, byte[] frame);

    /// <summary>记录硬件联调阶段的决策说明。Release 默认过滤，避免正式日志过于冗长。</summary>
    void HardwareDecision(string message);
}
