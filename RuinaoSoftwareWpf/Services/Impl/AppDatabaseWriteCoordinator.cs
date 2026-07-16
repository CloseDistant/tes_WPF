namespace RuinaoSoftwareWpf;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.IO;
using System.Diagnostics;

/// <summary>
/// 串行执行低频关键数据库写入，避免电刺激、EEG、数字表型同时争用 SQLite 写锁。
/// </summary>
public sealed class AppDatabaseWriteCoordinator : IAppDatabaseWriteCoordinator
{
    private const int MaxRetryCount = 3;
    private readonly SemaphoreSlim writeGate = new(1, 1);
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
                        + $"第 {attempt}/{MaxRetryCount - 1} 次。数据库：{Path.GetFileName(databasePath)}");
                    await Task.Delay(delay, cancellationToken);
                }
                catch (Exception exception)
                {
                    logger.Error($"SQLite 关键事件写入失败：{databasePath}", exception);
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
