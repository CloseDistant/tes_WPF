namespace RuinaoSoftwareWpf;

using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using System.IO;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Principal;

internal sealed class AuditTrailService : IAuditTrailService, IAuditTrailStore
{
    private const string SchemaVersionKey = "schema_version";
    private const string CurrentSchemaVersion = "2";
    private const int MigrationBatchSize = 500;
    private static readonly TimeSpan WriteFailureNotificationInterval = TimeSpan.FromSeconds(30);

    private readonly AuditTrailStorageOptions options;
    private readonly ILoggingService logger;
    private readonly TimeProvider timeProvider;
    private readonly SemaphoreSlim initializeGate = new(1, 1);
    private readonly SemaphoreSlim writeGate = new(1, 1);
    private readonly object notificationGate = new();
    private readonly string applicationSessionId = Guid.NewGuid().ToString("N");
    private readonly string softwareVersion;
    private int initialized;
    private int writesSuspended;
    private DateTimeOffset lastWriteFailureNotificationUtc = DateTimeOffset.MinValue;

    public AuditTrailService(
        AuditTrailStorageOptions options,
        ILoggingService logger,
        TimeProvider timeProvider)
    {
        this.options = options;
        this.logger = logger;
        this.timeProvider = timeProvider;
        softwareVersion = typeof(AuditTrailService).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? "1.0.0";
    }

    public event EventHandler<AuditTrailWriteFailedEventArgs>? WriteFailed;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref initialized) == 1)
        {
            return;
        }

        await initializeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref initialized) == 1)
            {
                return;
            }

            var directory = Path.GetDirectoryName(options.DatabasePath)
                ?? throw new InvalidOperationException("安全审计数据库目录无效");
            Directory.CreateDirectory(directory);
            if (options.ApplyDirectoryAcl)
            {
                ApplyRestrictedDirectoryAcl(directory);
            }

            await EncryptedSqliteDatabase.EnsureEncryptedAsync(
                    options.DatabasePath,
                    logger,
                    CopyPlaintextAuditDatabaseAsync,
                    cancellationToken)
                .ConfigureAwait(false);
            await EnsureCurrentSchemaAsync(cancellationToken).ConfigureAwait(false);
            DeleteObsoleteSecurityArtifact(Path.Combine(directory, "audit_hmac.key"));
            Volatile.Write(ref initialized, 1);
            logger.Info("安全审计服务已就绪，审计数据库采用整库加密。");
        }
        finally
        {
            initializeGate.Release();
        }
    }

    public async Task AppendAsync(AuditEventInput auditEvent, CancellationToken cancellationToken = default)
    {
        _ = await TryAppendAsync(auditEvent, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> TryAppendAsync(AuditEventInput auditEvent, CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref writesSuspended) == 1)
        {
            return true;
        }

        try
        {
            await AppendCoreAsync(auditEvent, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.Error("安全审计记录写入失败，业务操作继续执行", exception);
            NotifyWriteFailure();
            return false;
        }
    }

    public async Task<AuditQueryResult> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ValidateQuery(query);
        await InitializeAsync(cancellationToken).ConfigureAwait(false);

        var pageNumber = Math.Max(1, query.PageNumber);
        var pageSize = Math.Clamp(query.PageSize, 1, 200);
        var startMs = query.StartUtc.ToUniversalTime().ToUnixTimeMilliseconds();
        var endMs = query.EndUtc.ToUniversalTime().ToUnixTimeMilliseconds();
        await using var context = new AuditTrailDbContext(options.DatabasePath);
        var events = context.Events.AsNoTracking().Where(item =>
            item.OccurredAtUtcMs >= startMs
            && item.OccurredAtUtcMs <= endMs
            && item.EventCategory != (int)AuditEventCategory.AuditSystem);
        if (query.Category is { } category)
        {
            events = events.Where(item => item.EventCategory == (int)category);
        }
        if (!string.IsNullOrWhiteSpace(query.ActorLoginName))
        {
            var actor = query.ActorLoginName.Trim();
            events = events.Where(item => item.ActorLoginName == actor);
        }

        var totalCount = await events.LongCountAsync(cancellationToken).ConfigureAwait(false);
        var entities = await events
            .OrderByDescending(item => item.SequenceNo)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return new AuditQueryResult(entities.Select(ToRecord).ToList(), totalCount, pageNumber, pageSize);
    }

    public async Task<IReadOnlyList<string>> GetActorLoginNamesAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var context = new AuditTrailDbContext(options.DatabasePath);
        return await context.Events
            .AsNoTracking()
            .Where(item => item.EventCategory != (int)AuditEventCategory.AuditSystem
                && item.ActorLoginName != "system")
            .Select(item => item.ActorLoginName)
            .Distinct()
            .OrderBy(item => item)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    internal void SuspendWrites() => Volatile.Write(ref writesSuspended, 1);

    internal void ResumeWrites() => Volatile.Write(ref writesSuspended, 0);

    private async Task AppendCoreAsync(AuditEventInput auditEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var context = new AuditTrailDbContext(options.DatabasePath);
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            var sequenceNo = (await context.Events.MaxAsync(
                item => (long?)item.SequenceNo,
                cancellationToken).ConfigureAwait(false) ?? 0L) + 1L;
            var occurredAtUtc = (auditEvent.OccurredAtUtc ?? timeProvider.GetUtcNow()).ToUniversalTime();
            context.Events.Add(new AuditEventEntity
            {
                SequenceNo = sequenceNo,
                EventId = Guid.NewGuid().ToString("D"),
                OccurredAtUtcMs = occurredAtUtc.ToUnixTimeMilliseconds(),
                ActorUserId = auditEvent.Actor.UserId,
                ActorLoginName = Sanitize(auditEvent.Actor.LoginName, 64, "system"),
                ActorRoleId = auditEvent.Actor.RoleId,
                SessionId = applicationSessionId,
                EventCategory = (int)auditEvent.Category,
                ActionCode = Sanitize(AuditActionCatalog.NormalizeActionCode(auditEvent.ActionCode), 64, "UNKNOWN_ACTION"),
                TargetType = Sanitize(auditEvent.TargetType, 40, "None"),
                TargetId = Sanitize(auditEvent.TargetId, 96, string.Empty),
                Result = (int)auditEvent.Result,
                FailureCode = Sanitize(auditEvent.FailureCode, 64, string.Empty),
                Reason = Sanitize(auditEvent.Reason, 256, string.Empty),
                WorkstationId = Sanitize(Environment.MachineName, 128, "unknown"),
                SoftwareVersion = softwareVersion
            });
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            writeGate.Release();
        }
    }

    private void NotifyWriteFailure()
    {
        var now = timeProvider.GetUtcNow();
        lock (notificationGate)
        {
            if (now - lastWriteFailureNotificationUtc < WriteFailureNotificationInterval)
            {
                return;
            }
            lastWriteFailureNotificationUtc = now;
        }

        try
        {
            WriteFailed?.Invoke(this, new AuditTrailWriteFailedEventArgs("安全审计记录写入失败，请联系管理员。"));
        }
        catch (Exception exception)
        {
            logger.Error("安全审计写入失败通知处理异常", exception);
        }
    }

    private async Task EnsureCurrentSchemaAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(options.DatabasePath) || new FileInfo(options.DatabasePath).Length == 0)
        {
            await CreateCurrentSchemaAsync(options.DatabasePath, cancellationToken).ConfigureAwait(false);
            return;
        }

        await using (var context = new AuditTrailDbContext(options.DatabasePath))
        {
            try
            {
                var version = await context.Metadata
                    .AsNoTracking()
                    .Where(item => item.Key == SchemaVersionKey)
                    .Select(item => item.Value)
                    .SingleOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (string.Equals(version, CurrentSchemaVersion, StringComparison.Ordinal))
                {
                    return;
                }
            }
            catch (SqliteException exception) when (exception.SqliteErrorCode == 1)
            {
                logger.Warning("审计数据库缺少版本信息，将迁移到当前加密结构。");
            }
        }

        await MigrateLegacySchemaAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task CopyPlaintextAuditDatabaseAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        await CreateCurrentSchemaAsync(destinationPath, cancellationToken).ConfigureAwait(false);
        long lastSequenceNo = 0;
        while (true)
        {
            List<AuditEventEntity> batch;
            await using (var source = new AuditTrailDbContext(sourcePath, encrypted: false))
            {
                batch = await source.Events
                    .AsNoTracking()
                    .Where(item => item.SequenceNo > lastSequenceNo)
                    .OrderBy(item => item.SequenceNo)
                    .Take(MigrationBatchSize)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            if (batch.Count == 0)
            {
                break;
            }

            await using (var target = new AuditTrailDbContext(destinationPath))
            {
                target.Events.AddRange(batch);
                await target.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            lastSequenceNo = batch[^1].SequenceNo;
        }
    }

    private async Task CreateCurrentSchemaAsync(string databasePath, CancellationToken cancellationToken)
    {
        await using var context = new AuditTrailDbContext(databasePath);
        await context.Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);
        context.Metadata.Add(new AuditMetadataEntity { Key = SchemaVersionKey, Value = CurrentSchemaVersion });
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task MigrateLegacySchemaAsync(CancellationToken cancellationToken)
    {
        var temporaryPath = $"{options.DatabasePath}.{Guid.NewGuid():N}.migrating";
        var rollbackPath = $"{options.DatabasePath}.{Guid.NewGuid():N}.rollback";
        try
        {
            await CreateCurrentSchemaAsync(temporaryPath, cancellationToken).ConfigureAwait(false);
            long copied = 0;
            while (true)
            {
                List<AuditEventEntity> batch;
                await using (var source = new AuditTrailDbContext(options.DatabasePath))
                {
                    batch = await source.Events
                        .AsNoTracking()
                        .Where(item => item.SequenceNo > copied)
                        .OrderBy(item => item.SequenceNo)
                        .Take(MigrationBatchSize)
                        .ToListAsync(cancellationToken)
                        .ConfigureAwait(false);
                }
                if (batch.Count == 0)
                {
                    break;
                }

                await using (var target = new AuditTrailDbContext(temporaryPath))
                {
                    target.Events.AddRange(batch);
                    await target.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                }
                copied = batch[^1].SequenceNo;
            }

            await using (var target = new AuditTrailDbContext(temporaryPath))
            {
                _ = await target.Events.LongCountAsync(cancellationToken).ConfigureAwait(false);
            }

            SqliteConnection.ClearAllPools();
            File.Replace(temporaryPath, options.DatabasePath, rollbackPath, ignoreMetadataErrors: true);
            await EncryptedSqliteDatabase.VerifyCanOpenAsync(options.DatabasePath, cancellationToken).ConfigureAwait(false);
            File.Delete(rollbackPath);
            logger.Info($"审计数据库已迁移到无应用层HMAC结构：events={copied}");
        }
        catch
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(rollbackPath))
            {
                File.Replace(rollbackPath, options.DatabasePath, null, ignoreMetadataErrors: true);
            }
            throw;
        }
        finally
        {
            TryDeleteFile(temporaryPath);
            TryDeleteFile(rollbackPath);
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Temporary files are harmless and can be removed during maintenance.
        }
    }

    private void DeleteObsoleteSecurityArtifact(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception)
        {
            logger.Warning($"旧安全文件清理失败：{exception.Message}");
        }
    }

    private static AuditEventRecord ToRecord(AuditEventEntity entity)
    {
        return new AuditEventRecord(
            entity.SequenceNo,
            entity.EventId,
            DateTimeOffset.FromUnixTimeMilliseconds(entity.OccurredAtUtcMs),
            entity.ActorUserId,
            entity.ActorLoginName,
            entity.ActorRoleId,
            entity.SessionId,
            (AuditEventCategory)entity.EventCategory,
            entity.ActionCode,
            entity.TargetType,
            entity.TargetId,
            (AuditEventResult)entity.Result,
            NullIfEmpty(entity.FailureCode),
            NullIfEmpty(entity.Reason),
            entity.WorkstationId,
            entity.SoftwareVersion);
    }

    private static void ValidateQuery(AuditQuery query)
    {
        if (query.EndUtc < query.StartUtc)
        {
            throw new ArgumentException("结束时间不能早于开始时间", nameof(query));
        }
        if (query.EndUtc - query.StartUtc > TimeSpan.FromDays(3660))
        {
            throw new ArgumentException("单次审计查询时间范围不能超过10年", nameof(query));
        }
    }

    private static void ApplyRestrictedDirectoryAcl(string directoryPath)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var currentUser = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("无法获取当前Windows账户标识");
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        AddFullControlRule(security, currentUser);
        AddFullControlRule(security, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null));
        AddFullControlRule(security, new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null));
        new DirectoryInfo(directoryPath).SetAccessControl(security);
    }

    private static void AddFullControlRule(DirectorySecurity security, SecurityIdentifier identity)
    {
        security.AddAccessRule(new FileSystemAccessRule(
            identity,
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
    }

    private static string Sanitize(string? value, int maximumLength, string fallback)
    {
        var sanitized = string.IsNullOrWhiteSpace(value)
            ? fallback
            : value.Trim().Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
        return sanitized.Length <= maximumLength ? sanitized : sanitized[..maximumLength];
    }

    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;
}
