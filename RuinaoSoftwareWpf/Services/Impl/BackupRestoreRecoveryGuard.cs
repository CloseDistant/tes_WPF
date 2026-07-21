namespace RuinaoSoftwareWpf;

using System.IO;
using System.Text.Json;

internal sealed record RestoreFileState(string TargetPath, string RollbackPath, bool OriginallyExisted);

internal sealed record RestoreRecoveryState(string OperationDirectory, IReadOnlyList<RestoreFileState> Files);

internal static class BackupRestoreRecoveryGuard
{
    private const string StateFileName = ".restore-pending.json";

    internal static string StatePath => Path.Combine(
        Path.GetDirectoryName(AppDatabasePathProvider.MainDatabasePath)!,
        StateFileName);

    internal static void WritePending(RestoreRecoveryState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StatePath)!);
        File.WriteAllText(StatePath, JsonSerializer.Serialize(state));
    }

    internal static bool TryRecoverPending(out string message)
    {
        message = string.Empty;
        if (!File.Exists(StatePath)) return true;
        try
        {
            var state = JsonSerializer.Deserialize<RestoreRecoveryState>(File.ReadAllText(StatePath))
                ?? throw new InvalidDataException("恢复状态文件无效");
            foreach (var file in state.Files)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file.TargetPath)!);
                if (file.OriginallyExisted)
                {
                    if (!File.Exists(file.RollbackPath))
                    {
                        throw new FileNotFoundException("恢复回滚副本缺失", file.RollbackPath);
                    }
                    File.Copy(file.RollbackPath, file.TargetPath, true);
                }
                else if (File.Exists(file.TargetPath))
                {
                    File.Delete(file.TargetPath);
                }
            }
            File.Delete(StatePath);
            TryDeleteDirectory(state.OperationDirectory);
            message = "检测到上次未完成的数据恢复，已自动回滚到恢复前数据。";
            return true;
        }
        catch (Exception exception)
        {
            message = $"未完成的数据恢复无法自动回滚：{exception.Message}";
            return false;
        }
    }

    internal static void Complete() => File.Delete(StatePath);

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
        catch
        {
        }
    }
}
