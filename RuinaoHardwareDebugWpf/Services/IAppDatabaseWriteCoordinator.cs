namespace RuinaoHardwareDebugWpf;

/// <summary>
/// 应用数据库关键事件的全局单写者。
/// 实时原始数据不经过这里：EEG、音视频写分段文件，温度和阻抗保存在内存。
/// </summary>
public interface IAppDatabaseWriteCoordinator
{
    Task ExecuteAsync(
        string databasePath,
        Func<Task> operation,
        CancellationToken cancellationToken = default);

    Task<T> ExecuteAsync<T>(
        string databasePath,
        Func<Task<T>> operation,
        CancellationToken cancellationToken = default);
}
