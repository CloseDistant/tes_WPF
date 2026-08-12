namespace RuinaoSoftwareWpf;

using System.IO;
using System.Security;

internal static class TrustedExecutablePath
{
    internal static string RequireBundledTool(string relativePath, string? applicationDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
        {
            throw new SecurityException("外部程序路径必须是软件工具目录内的相对路径。");
        }

        try
        {
            var root = Path.GetFullPath(applicationDirectory ?? AppContext.BaseDirectory);
            return RequireExecutableUnderToolsRoot(Path.Combine(root, "Tools", relativePath), root);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            throw new SecurityException("外部程序路径无效。", exception);
        }
    }

    internal static string RequireTrustedToolPath(string executablePath, string? applicationDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new SecurityException("外部程序路径不能为空。");
        }

        try
        {
            var root = Path.GetFullPath(applicationDirectory ?? AppContext.BaseDirectory);
            var fullPath = Path.GetFullPath(
                Path.IsPathRooted(executablePath)
                    ? executablePath
                    : Path.Combine(root, executablePath));
            return RequireExecutableUnderToolsRoot(fullPath, root);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            throw new SecurityException("外部程序路径无效。", exception);
        }
    }

    private static string RequireExecutableUnderToolsRoot(string executablePath, string applicationRoot)
    {
        var toolsRoot = Path.GetFullPath(Path.Combine(applicationRoot, "Tools"));
        var toolsRootWithSeparator = Path.EndsInDirectorySeparator(toolsRoot)
            ? toolsRoot
            : toolsRoot + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(executablePath);
        if (!fullPath.StartsWith(toolsRootWithSeparator, StringComparison.OrdinalIgnoreCase)
            || !Path.GetExtension(fullPath).Equals(".exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new SecurityException("仅允许运行软件工具目录内的可执行文件。");
        }

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("软件所需的外部程序不存在。", fullPath);
        }

        return fullPath;
    }
}
