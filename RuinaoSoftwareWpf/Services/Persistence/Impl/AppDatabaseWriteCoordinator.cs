namespace RuinaoSoftwareWpf;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.IO;
using System.Diagnostics;
using System.Collections.Concurrent;

/// <summary>
/// 按数据库路径串行执行运行期写入，避免不同业务模块同时争用同一个 SQLite 写锁。
/// </summary>
public sealed class AppDatabaseWriteCoordinator : IAppDatabaseWriteCoordinator
{
    private const int MaxRetryCount = 3;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> writeGates =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ILoggingService logger;
    private readonly IRuntimeTelemetryService telemetry;

    public AppDatabaseWriteCoordinator(ILoggingService logger, IRuntimeTelemetryService telemetry)
    {
        this.logger = logger;
        this.telemetry = telemetry;
    }

    public async Task ExecuteAsync(
        string databasePath,
        Func<Task> operation,
        CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(databasePath, async () =>
        {
            await operation();
            return true;
        }, cancellationToken);
    }

    public async Task<T> ExecuteAsync<T>(
        string databasePath,
        Func<Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentNullException.ThrowIfNull(operation);

        var normalizedPath = Path.GetFullPath(databasePath);
        var writeGate = writeGates.GetOrAdd(normalizedPath, static _ => new SemaphoreSlim(1, 1));
        await writeGate.WaitAsync(cancellationToken);
        var startedAt = Stopwatch.GetTimestamp();
        try
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    return await operation();
                }
                catch (Exception exception) when (IsRetryableSqliteException(exception) && attempt < MaxRetryCount)
                {
                    var delay = TimeSpan.FromMilliseconds(120 * attempt);
                    logger.Warning(
                        $"SQLite 关键事件写入被占用，{delay.TotalMilliseconds:0}ms 后重试，"
                        + $"第 {attempt}/{MaxRetryCount - 1} 次。数据库：{Path.GetFileName(normalizedPath)}");
                    await Task.Delay(delay, cancellationToken);
                }
                catch (Exception exception)
                {
                    logger.Error($"SQLite 数据库写入失败：{normalizedPath}", exception);
                    throw;
                }
            }
        }
        finally
        {
            telemetry.RecordDatabaseCommitDelay(Stopwatch.GetElapsedTime(startedAt));
            writeGate.Release();
        }
    }

    private static bool IsRetryableSqliteException(Exception exception)
    {
        if (exception is DbUpdateException dbUpdateException && dbUpdateException.InnerException is not null)
        {
            return IsRetryableSqliteException(dbUpdateException.InnerException);
        }

        return exception is SqliteException { SqliteErrorCode: 5 or 6 }
            || exception.Message.Contains("database is locked", StringComparison.OrdinalIgnoreCase)
            || exception.Message.Contains("database table is locked", StringComparison.OrdinalIgnoreCase);
    }
}
