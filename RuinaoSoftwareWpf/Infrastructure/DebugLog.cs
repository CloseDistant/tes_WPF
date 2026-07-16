using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace RuinaoSoftwareWpf;

/// <summary>
/// 应用日志级别。
/// 数值越小越详细，Release 构建会自动过滤低级别日志。
/// </summary>
public enum AppLogLevel
{
    Trace = 0,
    Debug = 1,
    Info = 2,
    Warning = 3,
    Error = 4
}

/// <summary>
/// 应用会话日志工具。
///
/// Debug 构建：
/// - 打开独立控制台窗口，实时显示日志。
/// - 写入 debug-*.log 文件。
/// - 记录 Debug 及以上级别。
/// - TX/RX 原始帧、硬件决策等细节会写入日志。
///
/// Release 构建：
/// - 不打开控制台窗口。
/// - 写入 release-*.log 文件。
/// - 记录 Info 及以上级别。
/// - 过滤 Debug/Trace 和硬件原始帧细节，只保留业务、审计、警告、错误日志。
///
/// 日志位置：
/// %USERPROFILE%\Documents\ruinaoLog\RuinaoSoftwareWpf\logs
/// </summary>
public static class DebugLog
{
    private const long DebugMaxBytes = 100L * 1024 * 1024;
    private const long ReleaseMaxBytes = 200L * 1024 * 1024;
    private const int DebugRetentionDays = 14;
    private const int ReleaseRetentionDays = 90;

    private static readonly object SyncRoot = new();
    private static readonly string SessionStamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
    private static bool initialized;
    private static bool fileLoggingAvailable;

    /// <summary>当前构建模式，写入日志头便于排查。</summary>
    public static string BuildMode
    {
        get
        {
#if DEBUG
            return "Debug";
#else
            return "Release";
#endif
        }
    }

    /// <summary>日志文件存放目录。</summary>
    public static string LogDirectory { get; private set; } = ResolvePreferredLogDirectory();

    /// <summary>当前会话日志文件完整路径。</summary>
    public static string CurrentLogPath { get; private set; } = Path.Combine(
        ResolvePreferredLogDirectory(),
#if DEBUG
        $"debug-{SessionStamp}.log");
#else
        $"release-{SessionStamp}.log");
#endif

    /// <summary>写 Trace 级别日志。仅 Debug 构建记录。</summary>
    public static void WriteTrace(string message)
    {
        Write(AppLogLevel.Trace, message);
    }

    /// <summary>写 Debug 级别日志。Release 构建默认过滤。</summary>
    public static void WriteLine(string message)
    {
        Write(AppLogLevel.Debug, message);
    }

    /// <summary>写 Info 级别日志。</summary>
    public static void WriteInfo(string message)
    {
        Write(AppLogLevel.Info, message);
    }

    /// <summary>写 Warning 级别日志。</summary>
    public static void WriteWarning(string message)
    {
        Write(AppLogLevel.Warning, message);
    }

    /// <summary>写 Error 级别日志，可附带异常堆栈。</summary>
    public static void WriteError(string message, Exception? exception = null)
    {
        Write(AppLogLevel.Error, exception is null ? message : $"{message}{Environment.NewLine}{exception}");
    }

    /// <summary>
    /// 写硬件业务日志。
    /// 这类日志在 Release 中保留，用于确认关键硬件动作是否发生。
    /// </summary>
    public static void WriteHardwareCommunication(string message)
    {
        Write(AppLogLevel.Info, $"[硬件通信] {message}");
    }

    /// <summary>
    /// 写硬件 TX 原始帧。
    /// 原始帧很容易刷屏，Release 默认过滤，仅 Debug 构建保留。
    /// </summary>
    public static void WriteHardwareTx(string command, byte[] frame)
    {
        WriteHardwareVerbose($"TX command={command} bytes={Convert.ToHexString(frame)}");
    }

    /// <summary>
    /// 写硬件 RX 原始帧。
    /// 原始帧很容易刷屏，Release 默认过滤，仅 Debug 构建保留。
    /// </summary>
    public static void WriteHardwareRx(string source, byte[] frame)
    {
        WriteHardwareVerbose($"RX source={source} bytes={Convert.ToHexString(frame)}");
    }

    /// <summary>
    /// 写硬件决策或协议说明。
    /// Release 默认过滤，避免正式日志包含过多开发解释。
    /// </summary>
    public static void WriteHardwareDecision(string message)
    {
        WriteHardwareVerbose($"DECISION {message}");
    }

    private static void WriteHardwareVerbose(string message)
    {
        Write(AppLogLevel.Debug, $"[硬件细节] {message}");
    }

    private static void Write(AppLogLevel level, string message)
    {
        if (level < MinimumFileLevel)
        {
            WriteConsoleOnly(level, message);
            return;
        }

        EnsureInitialized();

        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
        lock (SyncRoot)
        {
            if (fileLoggingAvailable)
            {
                try
                {
                    File.AppendAllText(CurrentLogPath, line + Environment.NewLine, Encoding.UTF8);
                }
                catch
                {
                    fileLoggingAvailable = false;
                }
            }
        }

#if DEBUG
        Console.WriteLine(line);
#endif
    }

    private static AppLogLevel MinimumFileLevel
    {
        get
        {
#if DEBUG
            return AppLogLevel.Debug;
#else
            return AppLogLevel.Info;
#endif
        }
    }

    private static int RetentionDays
    {
        get
        {
#if DEBUG
            return DebugRetentionDays;
#else
            return ReleaseRetentionDays;
#endif
        }
    }

    private static long MaxBytes
    {
        get
        {
#if DEBUG
            return DebugMaxBytes;
#else
            return ReleaseMaxBytes;
#endif
        }
    }

    private static void EnsureInitialized()
    {
        if (initialized)
        {
            return;
        }

        lock (SyncRoot)
        {
            if (initialized)
            {
                return;
            }

            fileLoggingAvailable = TryInitializeFileLogging();

#if DEBUG
            AllocConsole();
            Console.Title = "Ruinao Hardware Debug Log";
            Console.WriteLine("Ruinao WPF hardware debug log");
            Console.WriteLine($"Build mode: {BuildMode}");
            Console.WriteLine($"Minimum file level: {MinimumFileLevel}");
            Console.WriteLine(fileLoggingAvailable ? $"Log file: {CurrentLogPath}" : "File logging unavailable; console only.");
            Console.WriteLine("--------------------------------");
#endif

            initialized = true;
        }
    }

    private static void WriteConsoleOnly(AppLogLevel level, string message)
    {
#if DEBUG
        EnsureInitialized();
        Console.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}");
#endif
    }

    private static void CleanupOldLogs()
    {
        var directory = new DirectoryInfo(LogDirectory);
        if (!directory.Exists)
        {
            return;
        }

        var cutoff = DateTime.Now.AddDays(-RetentionDays);
        foreach (var file in directory.GetFiles("*.log").Where(file => file.LastWriteTime < cutoff))
        {
            TryDelete(file);
        }

        var files = directory.GetFiles("*.log")
            .OrderByDescending(file => file.LastWriteTime)
            .ToList();

        var totalBytes = files.Sum(file => file.Length);
        foreach (var file in files.OrderBy(file => file.LastWriteTime))
        {
            if (totalBytes <= MaxBytes)
            {
                break;
            }

            totalBytes -= file.Length;
            TryDelete(file);
        }
    }

    private static bool TryInitializeFileLogging()
    {
        foreach (var directory in CandidateLogDirectories().Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                LogDirectory = directory;
                CurrentLogPath = Path.Combine(directory, CurrentLogFileName());
                Directory.CreateDirectory(directory);
                CleanupOldLogs();
                File.AppendAllText(
                    CurrentLogPath,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [Info] 日志会话创建 build={BuildMode} minLevel={MinimumFileLevel}{Environment.NewLine}",
                    Encoding.UTF8);
                return true;
            }
            catch
            {
                // 日志是辅助能力。目录不可写时继续尝试降级目录，不能阻止软件启动。
            }
        }

        return false;
    }

    private static IEnumerable<string> CandidateLogDirectories()
    {
        yield return ResolvePreferredLogDirectory();
        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Ruinao",
            "RuinaoSoftwareWpf",
            "logs");
        yield return Path.Combine(Path.GetTempPath(), "RuinaoSoftwareWpf", "logs");
    }

    private static string ResolvePreferredLogDirectory()
    {
        var configured = Environment.GetEnvironmentVariable("RUINAO_LOG_DIRECTORY");
        return !string.IsNullOrWhiteSpace(configured)
            ? Path.GetFullPath(configured)
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "ruinaoLog",
                "RuinaoSoftwareWpf",
                "logs");
    }

    private static string CurrentLogFileName()
    {
#if DEBUG
        return $"debug-{SessionStamp}.log";
#else
        return $"release-{SessionStamp}.log";
#endif
    }

    private static void TryDelete(FileInfo file)
    {
        try
        {
            file.Delete();
        }
        catch
        {
            // 日志清理失败不应影响软件启动。
        }
    }

#if DEBUG
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AllocConsole();
#endif
}
