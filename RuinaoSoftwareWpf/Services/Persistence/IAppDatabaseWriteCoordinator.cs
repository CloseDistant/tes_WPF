namespace RuinaoSoftwareWpf;

/// <summary>
/// 应用运行期数据库提交的统一写入协调器。
/// 同一数据库串行提交，不同数据库互不阻塞。
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
