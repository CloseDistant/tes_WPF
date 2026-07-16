namespace RuinaoSoftwareWpf;

using System.IO;
using System.Runtime.InteropServices;

public sealed class DesktopShortcutService : IDesktopShortcutService
{
    private const string ShortcutName = "经颅直流电刺激上位机软件.lnk";
    private readonly ILoggingService logger;
    private readonly string shortcutPath;

    public DesktopShortcutService(ILoggingService logger)
    {
        this.logger = logger;
        shortcutPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            ShortcutName);
    }

    public DesktopShortcutResult CreateOrUpdate()
    {
        object? shell = null;
        object? shortcut = null;
        try
        {
            var executablePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
            {
                return new DesktopShortcutResult(false, "无法确定当前软件的可执行文件路径。");
            }

            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null)
            {
                return new DesktopShortcutResult(false, "当前 Windows 环境不支持创建桌面快捷方式。");
            }

            shell = Activator.CreateInstance(shellType)
                ?? throw new InvalidOperationException("无法启动 Windows 快捷方式服务。");
            shortcut = ((dynamic)shell).CreateShortcut(shortcutPath);
            dynamic shortcutObject = shortcut;
            shortcutObject.TargetPath = executablePath;
            shortcutObject.WorkingDirectory = Path.GetDirectoryName(executablePath) ?? AppContext.BaseDirectory;
            shortcutObject.IconLocation = executablePath + ",0";
            shortcutObject.Description = "经颅直流电刺激上位机软件";
            shortcutObject.Save();

            logger.Info($"桌面快捷方式已创建或更新：{shortcutPath} -> {executablePath}");
            return new DesktopShortcutResult(true, "桌面快捷方式已创建。", shortcutPath);
        }
        catch (Exception exception)
        {
            logger.Error("创建桌面快捷方式失败", exception);
            return new DesktopShortcutResult(false, $"创建桌面快捷方式失败：{exception.Message}");
        }
        finally
        {
            ReleaseComObject(shortcut);
            ReleaseComObject(shell);
        }
    }

    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            Marshal.FinalReleaseComObject(value);
        }
    }
}
