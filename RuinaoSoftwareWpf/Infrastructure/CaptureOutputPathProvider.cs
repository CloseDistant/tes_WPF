namespace RuinaoSoftwareWpf;

using System.Globalization;
using System.IO;

/// <summary>
/// 采集工作台输出路径与会话编号工具。
/// 统一维护本地输出目录，避免 View 和 ViewModel 各自硬编码。
/// </summary>
public static class CaptureOutputPathProvider
{
    public static string GetOutputRoot()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "ruinao",
            "capture_output");
    }

    public static string CreateSessionKey(DateTimeOffset timestamp)
    {
        return timestamp.LocalDateTime.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
    }
}
