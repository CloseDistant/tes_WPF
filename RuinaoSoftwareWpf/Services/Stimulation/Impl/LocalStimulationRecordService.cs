namespace RuinaoSoftwareWpf;

using Microsoft.EntityFrameworkCore;
using System.Text.Json;

public sealed class LocalStimulationRecordService : IStimulationRecordService, IDisposable
{
    private const string DefaultAdverseReactionRecord = "无不良反应记录";
    private const string RunningStatus = StimulationTreatmentLifecycle.RunningStatus;
    private const string EndedStatus = StimulationTreatmentLifecycle.EndedStatus;
    private const string IncompleteStatus = StimulationTreatmentLifecycle.IncompleteStatus;
    private const string NormalCompletionEndType = StimulationTreatmentLifecycle.NormalCompletionEndType;
    private const string ManualTerminationEndType = StimulationTreatmentLifecycle.ManualTerminationEndType;
    private const string AbnormalTerminationEndType = StimulationTreatmentLifecycle.AbnormalTerminationEndType;

    private readonly IPatientService patientService;
    private readonly IAppDatabaseInitializer databaseInitializer;
    private readonly IUnifiedSessionService unifiedSessionService;
    private readonly IAppDatabaseWriteCoordinator databaseWriteCoordinator;
    private readonly IAuthorizationService authorizationService;
    private readonly SemaphoreSlim recoveryLock = new(1, 1);
    private bool recoveryCompleted;
    private bool disposed;

    public LocalStimulationRecordService(
        IPatientService patientService,
        IAppDatabaseInitializer databaseInitializer,
        IUnifiedSessionService unifiedSessionService,
        IAppDatabaseWriteCoordinator databaseWriteCoordinator,
        IAuthorizationService authorizationService)
    {
        this.patientService = patientService;
        this.databaseInitializer = databaseInitializer;
        this.unifiedSessionService = unifiedSessionService;
        this.databaseWriteCoordinator = databaseWriteCoordinator;
        this.authorizationService = authorizationService;
    }

    public async Task<string> StartRunAsync(
        StimulationRunStartRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateStartRequest(request);
        var currentUser = authorizationService.RequireSignedIn();
        await EnsureRecoveredAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var runId = Guid.NewGuid().ToString("N");
        var patientCode = patientService.CurrentPatient?.PatientCode;
        var databasePath = AppDatabasePathProvider.MainDatabasePath;
        await databaseWriteCoordinator.ExecuteAsync(databasePath, async () =>
        {
            await using var context = new CaptureDbContext(databasePath);
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            var run = new StimulationRunEntity
            {
                RunId = runId,
                OperatorUserId = currentUser.UserId,
                PatientCode = patientCode,
                StimulationType = request.StimulationType.Trim(),
                PrescriptionName = NullIfWhiteSpace(request.PrescriptionName),
                GroupTitle = request.GroupTitle.Trim(),
                Status = RunningStatus,
                StartedAtUnixMs = now,
                CreatedAtUnixMs = now,
                UpdatedAtUnixMs = now
            };
            foreach (var channel in request.Channels)
            {
                run.Channels.Add(new StimulationChannelTreatmentEntity
                {
                    ChannelName = channel.ChannelName.Trim(),
                    Status = RunningStatus,
                    StartedAtUnixMs = now,
                    CurrentMilliamp = channel.CurrentMilliamp,
                    PlannedDurationSeconds = channel.PlannedDurationSeconds,
                    Polarity = channel.Polarity,
                    ParameterSchemaVersion = StimulationRecordParameters.CurrentSnapshotSchemaVersion,
                    ParameterSnapshotJson = channel.ParameterSnapshotJson,
                    PlannedTotalCount = channel.PlannedTotalCount,
                    CreatedAtUnixMs = now,
                    UpdatedAtUnixMs = now
                });
            }

            context.StimulationRuns.Add(run);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }, cancellationToken);

        await RecordSessionEventIfAvailableAsync(
            "start",
            request.GroupTitle,
            request.Channels.Select(item => item.ChannelName),
            runId,
            cancellationToken);
        return runId;
    }

    public async Task EndChannelsAsync(
        StimulationChannelsEndRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateEndRequest(request);
        var currentUser = authorizationService.RequireSignedIn();
        await EnsureRecoveredAsync(cancellationToken);

        var channelsToEnd = request.Channels
            .Where(item => !string.IsNullOrWhiteSpace(item.ChannelName))
            .GroupBy(item => item.ChannelName.Trim(), StringComparer.Ordinal)
            .Select(group => group.Last() with { ChannelName = group.Key })
            .ToArray();
        var channelNames = channelsToEnd.Select(item => item.ChannelName).ToArray();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var endType = StimulationTreatmentLifecycle.ToStorageCode(request.EndType);
        var databasePath = AppDatabasePathProvider.MainDatabasePath;
        var affectedRunIds = new HashSet<long>();
        var affectedRunKeys = new HashSet<string>(StringComparer.Ordinal);

        await databaseWriteCoordinator.ExecuteAsync(databasePath, async () =>
        {
            await using var context = new CaptureDbContext(databasePath);
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            foreach (var channelEnd in channelsToEnd)
            {
                var channel = await context.StimulationChannelTreatments
                    .Include(item => item.Run)
                    .Where(item =>
                        item.ChannelName == channelEnd.ChannelName
                        && item.Status == RunningStatus
                        && item.Run != null
                        && item.Run.OperatorUserId == currentUser.UserId
                        && item.Run.StimulationType == request.StimulationType)
                    .OrderByDescending(item => item.StartedAtUnixMs)
                    .ThenByDescending(item => item.Id)
                    .FirstOrDefaultAsync(cancellationToken);
                if (channel is null)
                {
                    continue;
                }

                channel.Status = EndedStatus;
                channel.EndedAtUnixMs = now;
                channel.EndType = endType;
                channel.EndReasonCode = request.EndReasonCode.Trim();
                channel.EndReasonDetail = NullIfWhiteSpace(request.EndReasonDetail);
                channel.CompletedCount = channelEnd.CompletedCount;
                channel.UpdatedAtUnixMs = now;
                affectedRunIds.Add(channel.StimulationRunId);
                if (channel.Run is not null)
                {
                    affectedRunKeys.Add(channel.Run.RunId);
                }
            }

            foreach (var runId in affectedRunIds)
            {
                var run = await context.StimulationRuns
                    .Include(item => item.Channels)
                    .SingleAsync(item => item.Id == runId, cancellationToken);
                StimulationTreatmentLifecycle.RecalculateRunState(run, now);
            }

            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }, cancellationToken);

        foreach (var runId in affectedRunKeys)
        {
            await RecordSessionEventIfAvailableAsync(
                request.EndReasonCode,
                request.StimulationType,
                channelNames,
                runId,
                cancellationToken);
        }
    }

    public async Task<PageResult<StimulationTreatmentRecord>> GetTreatmentRecordsPageAsync(
        PageRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var currentUser = authorizationService.RequireSignedIn();
        await EnsureRecoveredAsync(cancellationToken);
        await using var context = new CaptureDbContext(AppDatabasePathProvider.MainDatabasePath);
        var query = context.StimulationRuns
            .AsNoTracking()
            .Where(item => item.OperatorUserId == currentUser.UserId);
        var totalCount = await query.CountAsync(cancellationToken);
        var records = await query
            .Include(item => item.Channels)
            .OrderByDescending(item => item.StartedAtUnixMs)
            .ThenByDescending(item => item.Id)
            .Skip(request.SafeOffset)
            .Take(request.SafePageSize + 1)
            .ToListAsync(cancellationToken);
        var hasMore = records.Count > request.SafePageSize;
        if (hasMore)
        {
            records.RemoveAt(records.Count - 1);
        }

        var patientCodes = records
            .Select(item => item.PatientCode)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var patientEntities = await context.Patients
            .AsNoTracking()
            .Where(item => patientCodes.Contains(item.PatientCode))
            .ToListAsync(cancellationToken);
        var patients = patientEntities.ToDictionary(
            item => item.PatientCode,
            item => item.Name ?? item.PatientCode,
            StringComparer.Ordinal);

        var items = records.Select(item => MapTreatmentRecord(item, patients)).ToArray();
        return new PageResult<StimulationTreatmentRecord>(items, hasMore, totalCount);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        recoveryLock.Dispose();
        disposed = true;
    }

    private async Task EnsureRecoveredAsync(CancellationToken cancellationToken)
    {
        if (recoveryCompleted)
        {
            return;
        }

        await recoveryLock.WaitAsync(cancellationToken);
        try
        {
            if (recoveryCompleted)
            {
                return;
            }

            await databaseInitializer.EnsureInitializedAsync(cancellationToken);
            var databasePath = AppDatabasePathProvider.MainDatabasePath;
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            await databaseWriteCoordinator.ExecuteAsync(databasePath, async () =>
            {
                await using var context = new CaptureDbContext(databasePath);
                await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
                var runningChannels = await context.StimulationChannelTreatments
                    .Where(item => item.Status == RunningStatus)
                    .ToListAsync(cancellationToken);
                foreach (var channel in runningChannels)
                {
                    StimulationTreatmentLifecycle.MarkSoftwareInterrupted(channel, now);
                }

                var affectedRunIds = runningChannels
                    .Select(item => item.StimulationRunId)
                    .Distinct()
                    .ToArray();
                if (affectedRunIds.Length > 0)
                {
                    var runs = await context.StimulationRuns
                        .Where(item => affectedRunIds.Contains(item.Id))
                        .ToListAsync(cancellationToken);
                    foreach (var run in runs)
                    {
                        run.Status = IncompleteStatus;
                        run.EndedAtUnixMs = null;
                        run.UpdatedAtUnixMs = now;
                    }
                }

                await context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }, cancellationToken);
            recoveryCompleted = true;
        }
        finally
        {
            recoveryLock.Release();
        }
    }

    private async Task RecordSessionEventIfAvailableAsync(
        string eventType,
        string groupTitle,
        IEnumerable<string> channelNames,
        string runId,
        CancellationToken cancellationToken)
    {
        if (patientService.CurrentPatient is null)
        {
            return;
        }

        var session = await unifiedSessionService.GetOrStartAsync(cancellationToken);
        var payloadJson = JsonSerializer.Serialize(new
        {
            session.SessionKey,
            RunId = runId,
            GroupTitle = groupTitle,
            ChannelNames = channelNames.ToArray()
        });
        await unifiedSessionService.RecordEventAsync(
            SessionModuleCodes.Stimulation,
            eventType,
            $"{groupTitle}：{string.Join(", ", channelNames)}",
            payloadJson,
            cancellationToken: cancellationToken);
    }

    private static StimulationTreatmentRecord MapTreatmentRecord(
        StimulationRunEntity entity,
        IReadOnlyDictionary<string, string> patients)
    {
        var firstChannel = entity.Channels.OrderBy(item => item.Id).FirstOrDefault();
        var parameterRecord = StimulationRecordParameters.PrescriptionFromSnapshotJson(
                firstChannel?.ParameterSnapshotJson)
            ?? StimulationRecordParameters.CreateFallbackRecord(
                entity.Id,
                entity.GroupTitle,
                string.Join(", ", entity.Channels.Select(item => item.ChannelName)),
                entity.StimulationType,
                entity.PrescriptionName);
        var channels = entity.Channels
            .OrderBy(item => item.Id)
            .Select(item => new StimulationChannelTreatmentRecord(
                item.Id,
                item.ChannelName,
                ParseStatus(item.Status),
                DateTimeOffset.FromUnixTimeMilliseconds(item.StartedAtUnixMs),
                item.EndedAtUnixMs.HasValue
                    ? DateTimeOffset.FromUnixTimeMilliseconds(item.EndedAtUnixMs.Value)
                    : null,
                ParseEndType(item.EndType),
                item.EndReasonCode,
                item.EndReasonDetail,
                item.PlannedTotalCount,
                item.CompletedCount))
            .ToArray();

        return new StimulationTreatmentRecord(
            entity.Id,
            entity.RunId,
            GetPatientDisplay(entity.PatientCode, patients),
            entity.StimulationType,
            DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeMilliseconds(entity.StartedAtUnixMs).LocalDateTime),
            string.IsNullOrWhiteSpace(entity.PrescriptionName) ? parameterRecord.Name : entity.PrescriptionName,
            DefaultAdverseReactionRecord,
            parameterRecord,
            channels);
    }

    private static void ValidateStartRequest(StimulationRunStartRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.GroupTitle))
        {
            throw new ArgumentException("刺激组名称不能为空。", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.StimulationType))
        {
            throw new ArgumentException("刺激类型不能为空。", nameof(request));
        }

        if (request.Channels.Count == 0)
        {
            throw new ArgumentException("至少需要一个刺激通道。", nameof(request));
        }

        if (request.Channels.Any(item =>
                string.IsNullOrWhiteSpace(item.ChannelName)
                || string.IsNullOrWhiteSpace(item.ParameterSnapshotJson)))
        {
            throw new ArgumentException("刺激通道名称和参数快照不能为空。", nameof(request));
        }

        if (request.Channels
            .Select(item => item.ChannelName.Trim())
            .Distinct(StringComparer.Ordinal)
            .Count() != request.Channels.Count)
        {
            throw new ArgumentException("同一次启动中不能包含重复通道。", nameof(request));
        }
    }

    private static void ValidateEndRequest(StimulationChannelsEndRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.StimulationType))
        {
            throw new ArgumentException("刺激类型不能为空。", nameof(request));
        }

        if (request.Channels.Count == 0
            || request.Channels.Any(item => string.IsNullOrWhiteSpace(item.ChannelName)))
        {
            throw new ArgumentException("至少需要一个有效的停止通道。", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.EndReasonCode))
        {
            throw new ArgumentException("结束原因码不能为空。", nameof(request));
        }
    }

    private static StimulationTreatmentStatus ParseStatus(string status) => status switch
    {
        RunningStatus => StimulationTreatmentStatus.Running,
        EndedStatus => StimulationTreatmentStatus.Ended,
        IncompleteStatus => StimulationTreatmentStatus.Incomplete,
        _ => StimulationTreatmentStatus.Incomplete
    };

    private static StimulationEndType? ParseEndType(string? endType) => endType switch
    {
        NormalCompletionEndType => StimulationEndType.NormalCompletion,
        ManualTerminationEndType => StimulationEndType.ManualTermination,
        AbnormalTerminationEndType => StimulationEndType.AbnormalTermination,
        _ => null
    };

    private static string GetPatientDisplay(
        string? patientCode,
        IReadOnlyDictionary<string, string> patients)
    {
        if (string.IsNullOrWhiteSpace(patientCode))
        {
            return "null";
        }

        return patients.TryGetValue(patientCode, out var name) && !string.IsNullOrWhiteSpace(name)
            ? name
            : patientCode;
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
