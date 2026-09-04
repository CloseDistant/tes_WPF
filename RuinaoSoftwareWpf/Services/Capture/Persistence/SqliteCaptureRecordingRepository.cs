namespace RuinaoSoftwareWpf;

using Microsoft.EntityFrameworkCore;
using RuinaoSoftwareWpf.ApplicationContracts;
using System.IO;
using System.Text.Json;

/// <summary>
/// 采集工作台本地 SQLite 仓储实现。
/// 使用 EF Core SQLite，不在业务仓储中手写 INSERT/UPDATE/DELETE SQL。
/// </summary>
public sealed class SqliteCaptureRecordingRepository :
    ICaptureRecordingRepository,
    IEegRecordingRepository,
    IUnifiedSessionRepository,
    IAssessmentRunStore
{
    private readonly ILoggingService logger;
    private readonly IPatientService patientService;
    private readonly IAppDatabaseInitializer databaseInitializer;
    private readonly IAppDatabaseWriteCoordinator databaseWriteCoordinator;

    public SqliteCaptureRecordingRepository(
        ILoggingService logger,
        IPatientService patientService,
        IAppDatabaseInitializer databaseInitializer,
        IAppDatabaseWriteCoordinator databaseWriteCoordinator)
    {
        this.logger = logger;
        this.patientService = patientService;
        this.databaseInitializer = databaseInitializer;
        this.databaseWriteCoordinator = databaseWriteCoordinator;
    }

    public Task RecoverIncompleteSessionsAsync(long recoveredAtUnixMs, CancellationToken cancellationToken = default)
    {
        var databasePath = AppDatabasePathProvider.MainDatabasePath;
        return ExecuteWriteAsync(databasePath, async () =>
        {
            await using var context = await OpenContextAsync(databasePath, cancellationToken);
            var incomplete = await context.AssessmentSessions
                .Where(item => item.Status == "in_progress")
                .ToListAsync(cancellationToken);
            if (incomplete.Count == 0)
            {
                return;
            }

            foreach (var session in incomplete)
            {
                session.Status = "interrupted";
                session.EndedAtUnixMs ??= recoveredAtUnixMs;
                session.UpdatedAtUnixMs = recoveredAtUnixMs;
            }

            await context.SaveChangesAsync(cancellationToken);
            logger.Warning($"已恢复 {incomplete.Count} 条未正常结束的统一 Session，并标记为 interrupted。");
        }, cancellationToken);
    }

    public Task EnsureSessionAsync(UnifiedSessionContext session, CancellationToken cancellationToken = default)
    {
        var databasePath = AppDatabasePathProvider.MainDatabasePath;
        return ExecuteWriteAsync(databasePath, async () =>
        {
            await using var context = await OpenContextAsync(databasePath, cancellationToken);
            var exists = await context.AssessmentSessions.AnyAsync(
                item => item.SessionKey == session.SessionKey,
                cancellationToken);
            if (exists)
            {
                return;
            }

            var startedAtUnixMs = session.StartedAtUtc.ToUnixTimeMilliseconds();
            context.AssessmentSessions.Add(new AssessmentSessionEntity
            {
                SessionKey = session.SessionKey,
                PatientCode = session.PatientCode,
                StartedAtUnixMs = startedAtUnixMs,
                Status = "in_progress",
                UploadStatus = "local_only",
                CreatedAtUnixMs = startedAtUnixMs,
                UpdatedAtUnixMs = startedAtUnixMs
            });
            await context.SaveChangesAsync(cancellationToken);
        }, cancellationToken);
    }

    public Task RecordTimelineEventAsync(UnifiedSessionTimelineEvent timelineEvent, CancellationToken cancellationToken = default)
    {
        var databasePath = AppDatabasePathProvider.MainDatabasePath;
        return ExecuteWriteAsync(databasePath, async () =>
        {
            await using var context = await OpenContextAsync(databasePath, cancellationToken);
            var sessionId = await context.AssessmentSessions
                .Where(item => item.SessionKey == timelineEvent.SessionKey)
                .Select(item => item.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (sessionId == 0)
            {
                throw new InvalidOperationException($"未找到统一 Session：{timelineEvent.SessionKey}");
            }

            context.SessionTimelineEvents.Add(new SessionTimelineEventEntity
            {
                SessionId = sessionId,
                SessionKey = timelineEvent.SessionKey,
                ModuleCode = timelineEvent.ModuleCode,
                EventType = timelineEvent.EventType,
                SequenceNo = timelineEvent.SequenceNo,
                EventTimeUnixMs = timelineEvent.EventTimeUnixMs,
                SessionElapsedMs = timelineEvent.SessionElapsedMs,
                MonotonicTicks = timelineEvent.MonotonicTicks,
                MonotonicFrequency = timelineEvent.MonotonicFrequency,
                SourceTimeUnixMs = timelineEvent.SourceTimeUnixMs,
                Message = timelineEvent.Message,
                PayloadJson = timelineEvent.PayloadJson
            });
            await context.SaveChangesAsync(cancellationToken);
        }, cancellationToken);
    }

    public Task CompleteUnifiedSessionAsync(
        string sessionKey,
        string status,
        long endedAtUnixMs,
        CancellationToken cancellationToken = default)
    {
        var databasePath = AppDatabasePathProvider.MainDatabasePath;
        return ExecuteWriteAsync(databasePath, async () =>
        {
            await using var context = await OpenContextAsync(databasePath, cancellationToken);
            var session = await context.AssessmentSessions.FirstOrDefaultAsync(
                item => item.SessionKey == sessionKey,
                cancellationToken) ?? throw new InvalidOperationException($"未找到统一 Session：{sessionKey}");
            session.Status = status;
            session.EndedAtUnixMs = endedAtUnixMs;
            session.UpdatedAtUnixMs = endedAtUnixMs;
            await context.SaveChangesAsync(cancellationToken);
        }, cancellationToken);
    }

    public async Task<PageResult<UnifiedSessionTimelineEvent>> GetTimelinePageAsync(
        string sessionKey,
        PageRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var databasePath = AppDatabasePathProvider.MainDatabasePath;
        await using var context = await OpenContextAsync(databasePath, cancellationToken);
        var items = await context.SessionTimelineEvents
            .AsNoTracking()
            .Where(item => item.SessionKey == sessionKey)
            .OrderBy(item => item.SequenceNo)
            .ThenBy(item => item.Id)
            .Skip(request.SafeOffset)
            .Take(request.SafePageSize + 1)
            .Select(item => new UnifiedSessionTimelineEvent(
                item.SessionKey,
                item.ModuleCode,
                item.EventType,
                item.SequenceNo,
                item.EventTimeUnixMs,
                item.SessionElapsedMs,
                item.MonotonicTicks,
                item.MonotonicFrequency,
                item.SourceTimeUnixMs,
                item.Message ?? string.Empty,
                item.PayloadJson ?? string.Empty))
            .ToListAsync(cancellationToken);
        var hasMore = items.Count > request.SafePageSize;
        if (hasMore)
        {
            items.RemoveAt(items.Count - 1);
        }

        return new PageResult<UnifiedSessionTimelineEvent>(items, hasMore);
    }

    public async Task<CaptureSessionInfo> CreateModuleSessionAsync(
        string outputRoot,
        long? assessmentAttemptId,
        string sessionKey,
        string moduleCode,
        string moduleName,
        string cameraName,
        string rawVideoPath,
        string normalizedVideoPath,
        string audioPath,
        string mergedVideoPath,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(outputRoot);
        var databasePath = AppDatabasePathProvider.MainDatabasePath;
        var patientCode = await patientService.GetRequiredCurrentPatientCodeAsync(cancellationToken);

        return await ExecuteWriteAsync(databasePath, async () =>
        {
            var outputDirectory = Path.GetDirectoryName(rawVideoPath) ?? outputRoot;
            var now = DateTimeOffset.Now;

            await using var context = await OpenContextAsync(databasePath, cancellationToken);
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

            var workbenchSession = await EnsureWorkbenchSessionAsync(context, sessionKey, patientCode, now, cancellationToken);
            var moduleRecord = CaptureRecordEntityFactory.CreateTaskModuleRecord(
                workbenchSession.Id,
                assessmentAttemptId,
                moduleCode,
                moduleName,
                ResolveModuleType(moduleName),
                cameraName,
                outputDirectory,
                rawVideoPath,
                normalizedVideoPath,
                audioPath,
                mergedVideoPath,
                now);

            context.AssessmentModuleRecords.Add(moduleRecord);
            var moduleStartEvent = CaptureRecordEntityFactory.CreateModuleEvent(
                workbenchSession.Id,
                0,
                "module_record_start",
                $"{moduleName}模块记录开始",
                null,
                now);
            moduleStartEvent.ModuleRecord = moduleRecord;
            context.AssessmentEvents.Add(moduleStartEvent);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return new CaptureSessionInfo(
                moduleRecord.Id,
                workbenchSession.Id,
                moduleRecord.Id,
                assessmentAttemptId,
                sessionKey,
                moduleCode,
                moduleName,
                databasePath,
                outputDirectory,
                rawVideoPath,
                normalizedVideoPath,
                audioPath,
                mergedVideoPath);
        }, cancellationToken);
    }

    public Task CompleteSessionAsync(
        CaptureSessionInfo session,
        string status,
        string? message = null,
        CancellationToken cancellationToken = default)
    {
        return ExecuteWriteAsync(session.DatabasePath, async () =>
        {
            await using var context = await OpenContextAsync(session.DatabasePath, cancellationToken);
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            var now = DateTimeOffset.Now;
            var nowUnixMs = now.ToUnixTimeMilliseconds();

            var moduleRecord = await context.AssessmentModuleRecords.FirstOrDefaultAsync(item => item.Id == session.ModuleRecordId, cancellationToken)
                ?? throw new InvalidOperationException($"未找到模块记录：{session.ModuleRecordId}");
            moduleRecord.EndedAtUnixMs = nowUnixMs;
            moduleRecord.Status = status;
            moduleRecord.ResultSummary = message ?? string.Empty;
            moduleRecord.UpdatedAtUnixMs = nowUnixMs;
            context.AssessmentModuleRecords.Update(moduleRecord);

            context.AssessmentEvents.Add(CaptureRecordEntityFactory.CreateModuleEvent(
                session.WorkbenchSessionId,
                session.ModuleRecordId,
                "module_record_end",
                message ?? status,
                null,
                now));
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }, cancellationToken);
    }

    public Task RecordModuleEventAsync(
        CaptureSessionInfo session,
        string eventType,
        string? message = null,
        string? payloadJson = null,
        DateTimeOffset? startedAt = null,
        DateTimeOffset? endedAt = null,
        CancellationToken cancellationToken = default)
    {
        return ExecuteWriteAsync(session.DatabasePath, async () =>
        {
            await using var context = await OpenContextAsync(session.DatabasePath, cancellationToken);
            context.AssessmentEvents.Add(CaptureRecordEntityFactory.CreateModuleEvent(
                session.WorkbenchSessionId,
                session.ModuleRecordId,
                eventType,
                message,
                payloadJson,
                DateTimeOffset.Now,
                startedAt,
                endedAt));
            await context.SaveChangesAsync(cancellationToken);
        }, cancellationToken);
    }

    public async Task<CaptureFormRecordInfo> SaveFormModuleRecordAsync(
        string outputRoot,
        long assessmentAttemptId,
        string sessionKey,
        string moduleCode,
        string moduleName,
        string formPayloadJson,
        string status = "completed",
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(outputRoot);
        var databasePath = AppDatabasePathProvider.MainDatabasePath;
        var patientCode = await patientService.GetRequiredCurrentPatientCodeAsync(cancellationToken);

        return await ExecuteWriteAsync(databasePath, async () =>
        {
            var now = DateTimeOffset.Now;

            await using var context = await OpenContextAsync(databasePath, cancellationToken);
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            var workbenchSession = await EnsureWorkbenchSessionAsync(context, sessionKey, patientCode, now, cancellationToken);

            var moduleRecord = CaptureRecordEntityFactory.CreateFormModuleRecord(
                workbenchSession.Id,
                assessmentAttemptId,
                moduleCode,
                moduleName,
                formPayloadJson,
                status,
                now);
            context.AssessmentModuleRecords.Add(moduleRecord);
            var formSubmitEvent = CaptureRecordEntityFactory.CreateModuleEvent(
                workbenchSession.Id,
                0,
                "form_submit",
                $"{moduleName}表单提交",
                formPayloadJson,
                now);
            formSubmitEvent.ModuleRecord = moduleRecord;
            context.AssessmentEvents.Add(formSubmitEvent);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return new CaptureFormRecordInfo(
                workbenchSession.Id,
                moduleRecord.Id,
                assessmentAttemptId,
                sessionKey,
                moduleCode,
                moduleName,
                databasePath);
        }, cancellationToken);
    }

    public Task<EegRecordingInfo> CreateEegRecordingAsync(
        CaptureSessionInfo captureSession,
        string recordName,
        EegAcquisitionConfig config,
        string outputDirectory,
        int segmentSeconds,
        CancellationToken cancellationToken = default)
    {
        return ExecuteWriteAsync(captureSession.DatabasePath, async () =>
        {
            var now = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            await using var context = await OpenContextAsync(captureSession.DatabasePath, cancellationToken);
            var entity = new EegRecordingEntity
            {
                ModuleRecordId = captureSession.ModuleRecordId,
                RecordName = recordName,
                OutputDir = outputDirectory,
                ChannelCount = config.ChannelCount,
                SampleRateHz = config.SampleRateHz,
                PageSeconds = config.PageSeconds,
                SegmentSeconds = segmentSeconds,
                DataType = "float32",
                ChannelNamesJson = JsonSerializer.Serialize(config.ChannelNames),
                ConfigJson = JsonSerializer.Serialize(config),
                StartedAtUnixMs = now,
                Status = "recording"
            };
            context.EegRecordings.Add(entity);
            await context.SaveChangesAsync(cancellationToken);
            return new EegRecordingInfo(entity.Id, captureSession, recordName, outputDirectory, segmentSeconds);
        }, cancellationToken);
    }

    public Task AddEegDataSegmentAsync(
        EegRecordingInfo recording,
        EegDataSegmentInfo segment,
        CancellationToken cancellationToken = default)
    {
        return ExecuteWriteAsync(recording.CaptureSession.DatabasePath, async () =>
        {
            await using var context = await OpenContextAsync(recording.CaptureSession.DatabasePath, cancellationToken);
            context.EegDataSegments.Add(new EegDataSegmentEntity
            {
                EegRecordingId = recording.Id,
                SegmentIndex = segment.SegmentIndex,
                RelativePath = segment.RelativePath,
                StartSampleIndex = segment.StartSampleIndex,
                SampleCount = segment.SampleCount,
                StartedAtUnixMs = segment.StartedAtUnixMs,
                EndedAtUnixMs = segment.EndedAtUnixMs,
                ByteLength = segment.ByteLength,
                Status = segment.Status
            });
            await context.SaveChangesAsync(cancellationToken);
        }, cancellationToken);
    }

    public Task AddEegMarkersAsync(
        EegRecordingInfo recording,
        IReadOnlyList<EegMarkerRecord> markers,
        CancellationToken cancellationToken = default)
    {
        if (markers.Count == 0)
        {
            return Task.CompletedTask;
        }

        return ExecuteWriteAsync(recording.CaptureSession.DatabasePath, async () =>
        {
            await using var context = await OpenContextAsync(recording.CaptureSession.DatabasePath, cancellationToken);
            foreach (var marker in markers)
            {
                if (string.IsNullOrWhiteSpace(marker.Code))
                {
                    throw new InvalidOperationException("新 EEG Marker 的 Code 不能为空。");
                }

                context.EegMarkers.Add(new EegMarkerEntity
                {
                    EegRecordingId = recording.Id,
                    Name = marker.Name,
                    Shortcut = marker.Shortcut,
                    ColorHex = marker.Color.ToString(),
                    EventTimeUnixMs = marker.AbsoluteTimestampMs,
                    ExperimentElapsedMs = (long)marker.ExperimentTime.TotalMilliseconds,
                    SampleIndex = marker.SampleIndex,
                    PageIndex = marker.PageIndex,
                    PageSampleIndex = marker.PageSampleIndex,
                    Source = marker.Source,
                    MarkerCode = marker.Code
                });
            }

            await context.SaveChangesAsync(cancellationToken);
        }, cancellationToken);
    }

    public Task CompleteEegRecordingAsync(
        EegRecordingInfo recording,
        long sampleCount,
        string status,
        CancellationToken cancellationToken = default)
    {
        return ExecuteWriteAsync(recording.CaptureSession.DatabasePath, async () =>
        {
            await using var context = await OpenContextAsync(recording.CaptureSession.DatabasePath, cancellationToken);
            var entity = await context.EegRecordings.FirstOrDefaultAsync(item => item.Id == recording.Id, cancellationToken)
                ?? throw new InvalidOperationException($"未找到 EEG 采集记录：{recording.Id}");
            entity.SampleCount = sampleCount;
            entity.EndedAtUnixMs = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            entity.Status = status;
            context.EegRecordings.Update(entity);
            await context.SaveChangesAsync(cancellationToken);
        }, cancellationToken);
    }

    public async Task<AssessmentRunContext?> GetActiveRunAsync(
        string patientCode,
        int totalModuleCount,
        CancellationToken cancellationToken = default)
    {
        var databasePath = AppDatabasePathProvider.MainDatabasePath;
        await using var context = await OpenContextAsync(databasePath, cancellationToken);
        var run = await context.AssessmentRuns
            .AsNoTracking()
            .Where(item => item.PatientCode == patientCode && item.Status == "in_progress")
            .OrderByDescending(item => item.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (run is null)
        {
            return null;
        }

        ValidateRunShape(run, patientCode);
        return ToRunContext(run);
    }

    public Task<AssessmentRunContext> CreateRunAsync(
        string patientCode,
        int totalModuleCount,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken = default)
    {
        var databasePath = AppDatabasePathProvider.MainDatabasePath;
        return ExecuteWriteAsync(databasePath, async () =>
        {
            await using var context = await OpenContextAsync(databasePath, cancellationToken);
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            var existing = await context.AssessmentRuns
                .Where(item => item.PatientCode == patientCode && item.Status == "in_progress")
                .OrderByDescending(item => item.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (existing is not null)
            {
                throw new InvalidOperationException("当前患者已经存在进行中的评估，请重新加载后继续该评估。");
            }

            var startedAtUnixMs = startedAt.ToUnixTimeMilliseconds();
            var run = new AssessmentRunEntity
            {
                PatientCode = patientCode,
                Status = "in_progress",
                TotalModuleCount = totalModuleCount,
                NextModuleIndex = 0,
                StartedAtUnixMs = startedAtUnixMs,
                CreatedAtUnixMs = startedAtUnixMs,
                UpdatedAtUnixMs = startedAtUnixMs
            };
            context.AssessmentRuns.Add(run);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ToRunContext(run);
        }, cancellationToken);
    }

    public Task<AssessmentRunContext> ResumeRunAsync(
        long runId,
        string patientCode,
        int totalModuleCount,
        DateTimeOffset resumedAt,
        CancellationToken cancellationToken = default)
    {
        var databasePath = AppDatabasePathProvider.MainDatabasePath;
        return ExecuteWriteAsync(databasePath, async () =>
        {
            await using var context = await OpenContextAsync(databasePath, cancellationToken);
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            var run = await context.AssessmentRuns
                .FirstOrDefaultAsync(item => item.Id == runId, cancellationToken)
                ?? throw new InvalidOperationException("当前评估已经不存在，请返回评估入口重新加载。");
            ValidateRunShape(run, patientCode);
            if (!string.Equals(run.Status, "in_progress", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("当前评估已经结束，请返回评估入口重新加载。");
            }

            // 模块数量不再作为继续评估的门槛；刷新为当前流程数量，
            // 使旧记录也能按当前流程正确判断最终完成状态。
            run.TotalModuleCount = totalModuleCount;

            var resumedAtUnixMs = resumedAt.ToUnixTimeMilliseconds();
            var interruptedAttempts = await context.AssessmentModuleAttempts
                .Where(item => item.RunId == run.Id && (item.Status == "running" || item.Status == "saving"))
                .ToListAsync(cancellationToken);
            foreach (var attempt in interruptedAttempts)
            {
                attempt.Status = "cancelled_invalid";
                attempt.ErrorCode = "APPLICATION_INTERRUPTED";
                attempt.Message = "继续评估时发现未结束模块，本次尝试已作废并将从模块开头重新执行。";
                attempt.EndedAtUnixMs = resumedAtUnixMs;
                attempt.UpdatedAtUnixMs = resumedAtUnixMs;
            }

            run.UpdatedAtUnixMs = resumedAtUnixMs;
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ToRunContext(run);
        }, cancellationToken);
    }

    public Task<AssessmentModuleRunContext> StartModuleAsync(
        AssessmentModuleStartRequest request,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken = default)
    {
        var databasePath = AppDatabasePathProvider.MainDatabasePath;
        return ExecuteWriteAsync(databasePath, async () =>
        {
            await using var context = await OpenContextAsync(databasePath, cancellationToken);
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            var startedAtUnixMs = startedAt.ToUnixTimeMilliseconds();
            var run = await context.AssessmentRuns
                .FirstOrDefaultAsync(item => item.Id == request.RunId, cancellationToken)
                ?? throw new InvalidOperationException("当前评估上下文不可用，请返回评估入口重新加载。");
            ValidateRunShape(run, request.PatientCode);
            if (!string.Equals(run.Status, "in_progress", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("当前评估已经结束，请返回评估入口重新加载。");
            }

            // 以当前正式流程数量维护完成判断，不阻止已有评估继续执行。
            run.TotalModuleCount = request.TotalModuleCount;

            if (run.NextModuleIndex != request.ModuleIndex)
            {
                throw new InvalidOperationException($"必须按顺序执行评估，当前应执行第 {run.NextModuleIndex + 1} 个模块。");
            }

            var hasActiveAttempt = await context.AssessmentModuleAttempts.AnyAsync(
                item => item.RunId == run.Id && (item.Status == "running" || item.Status == "saving"),
                cancellationToken);
            if (hasActiveAttempt)
            {
                throw new InvalidOperationException("当前评估已有正在运行或保存中的模块。");
            }

            var attemptNumber = await context.AssessmentModuleAttempts
                .Where(item => item.RunId == run.Id && item.ModuleIndex == request.ModuleIndex)
                .Select(item => (int?)item.AttemptNumber)
                .MaxAsync(cancellationToken) ?? 0;
            var attempt = new AssessmentModuleAttemptEntity
            {
                RunId = run.Id,
                SessionKey = request.SessionKey,
                ModuleCode = request.ModuleCode,
                ModuleName = request.ModuleName,
                ModuleIndex = request.ModuleIndex,
                AttemptNumber = attemptNumber + 1,
                Status = "running",
                StartedAtUnixMs = startedAtUnixMs,
                CreatedAtUnixMs = startedAtUnixMs,
                UpdatedAtUnixMs = startedAtUnixMs
            };
            context.AssessmentModuleAttempts.Add(attempt);
            run.UpdatedAtUnixMs = startedAtUnixMs;
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new AssessmentModuleRunContext(
                run.Id,
                attempt.Id,
                attempt.AttemptNumber,
                request.PatientCode,
                request.SessionKey,
                request.ModuleCode,
                request.ModuleName,
                request.ModuleIndex,
                startedAt);
        }, cancellationToken);
    }

    private static AssessmentRunContext ToRunContext(AssessmentRunEntity run)
    {
        return new AssessmentRunContext(
            run.Id,
            run.PatientCode,
            Math.Clamp(run.NextModuleIndex, 0, run.TotalModuleCount),
            run.TotalModuleCount,
            DateTimeOffset.FromUnixTimeMilliseconds(run.StartedAtUnixMs));
    }

    private static void ValidateRunShape(
        AssessmentRunEntity run,
        string patientCode)
    {
        if (!string.Equals(run.PatientCode, patientCode, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("当前患者与评估记录不一致，请返回评估入口重新加载。");
        }
    }

    public Task MarkSavingAsync(
        long attemptId,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        return UpdateAttemptAsync(
            attemptId,
            "saving",
            null,
            null,
            null,
            updatedAt,
            advanceRun: false,
            cancellationToken);
    }

    public Task<AssessmentModuleResult> CompleteModuleAsync(
        long attemptId,
        string? resultJson,
        DateTimeOffset endedAt,
        CancellationToken cancellationToken = default)
    {
        return UpdateAttemptAsync(
            attemptId,
            "completed",
            resultJson,
            null,
            null,
            endedAt,
            advanceRun: true,
            cancellationToken);
    }

    public Task<AssessmentModuleResult> CancelModuleAsync(
        long attemptId,
        string reason,
        DateTimeOffset endedAt,
        CancellationToken cancellationToken = default)
    {
        return UpdateAttemptAsync(
            attemptId,
            "cancelled_invalid",
            null,
            "CANCELLED_BY_USER",
            reason,
            endedAt,
            advanceRun: false,
            cancellationToken);
    }

    public Task<AssessmentModuleResult> FailModuleAsync(
        long attemptId,
        string errorCode,
        string message,
        DateTimeOffset endedAt,
        CancellationToken cancellationToken = default)
    {
        return UpdateAttemptAsync(
            attemptId,
            "failed",
            null,
            errorCode,
            message,
            endedAt,
            advanceRun: false,
            cancellationToken);
    }

    private Task<AssessmentModuleResult> UpdateAttemptAsync(
        long attemptId,
        string status,
        string? resultJson,
        string? errorCode,
        string? message,
        DateTimeOffset changedAt,
        bool advanceRun,
        CancellationToken cancellationToken)
    {
        var databasePath = AppDatabasePathProvider.MainDatabasePath;
        return ExecuteWriteAsync(databasePath, async () =>
        {
            await using var context = await OpenContextAsync(databasePath, cancellationToken);
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            var attempt = await context.AssessmentModuleAttempts
                .FirstOrDefaultAsync(item => item.Id == attemptId, cancellationToken)
                ?? throw new InvalidOperationException($"未找到评估模块尝试：{attemptId}");
            if (attempt.Status is "completed" or "cancelled_invalid" or "failed")
            {
                throw new InvalidOperationException($"评估模块尝试已经结束：attempt={attemptId}, status={attempt.Status}");
            }

            var run = await context.AssessmentRuns
                .FirstOrDefaultAsync(item => item.Id == attempt.RunId, cancellationToken)
                ?? throw new InvalidOperationException($"未找到评估批次：{attempt.RunId}");
            if (!string.Equals(
                    run.PatientCode,
                    patientService.CurrentPatient?.PatientCode,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException("当前患者已变化，拒绝修改其他患者的评估尝试。");
            }

            var changedAtUnixMs = changedAt.ToUnixTimeMilliseconds();
            attempt.Status = status;
            attempt.ResultJson = resultJson;
            attempt.ErrorCode = errorCode;
            attempt.Message = message;
            attempt.UpdatedAtUnixMs = changedAtUnixMs;
            if (status != "saving")
            {
                attempt.EndedAtUnixMs = changedAtUnixMs;
            }

            if (advanceRun)
            {
                if (run.NextModuleIndex != attempt.ModuleIndex)
                {
                    throw new InvalidOperationException("评估进度与完成模块不一致，拒绝推进批次。");
                }

                run.NextModuleIndex++;
                if (run.NextModuleIndex >= run.TotalModuleCount)
                {
                    run.Status = "completed";
                    run.EndedAtUnixMs = changedAtUnixMs;
                }
            }

            run.UpdatedAtUnixMs = changedAtUnixMs;
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new AssessmentModuleResult(
                attempt.RunId,
                attempt.Id,
                ParseAttemptStatus(status),
                DateTimeOffset.FromUnixTimeMilliseconds(attempt.StartedAtUnixMs),
                changedAt,
                errorCode,
                message);
        }, cancellationToken);
    }

    private static AssessmentModuleExecutionStatus ParseAttemptStatus(string status)
    {
        return status switch
        {
            "saving" => AssessmentModuleExecutionStatus.Saving,
            "completed" => AssessmentModuleExecutionStatus.Completed,
            "cancelled_invalid" => AssessmentModuleExecutionStatus.CancelledInvalid,
            "failed" => AssessmentModuleExecutionStatus.Failed,
            _ => AssessmentModuleExecutionStatus.Running
        };
    }

    private async Task<CaptureDbContext> OpenContextAsync(string databasePath, CancellationToken cancellationToken)
    {
        await EnsureDatabaseInitializedAsync(databasePath, cancellationToken);
        return new CaptureDbContext(databasePath);
    }

    private async Task EnsureDatabaseInitializedAsync(string databasePath, CancellationToken cancellationToken)
    {
        if (!string.Equals(databasePath, AppDatabasePathProvider.MainDatabasePath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("采集记录必须写入统一应用数据库。");
        }

        await databaseInitializer.EnsureInitializedAsync(cancellationToken);
    }

    private static async Task<AssessmentSessionEntity> EnsureWorkbenchSessionAsync(
        CaptureDbContext context,
        string sessionKey,
        string patientCode,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var nowUnixMs = now.ToUnixTimeMilliseconds();
        var session = await context.AssessmentSessions.FirstOrDefaultAsync(item => item.SessionKey == sessionKey, cancellationToken);
        if (session is not null)
        {
            if (!string.Equals(session.PatientCode, patientCode, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("SessionKey 已关联其他患者，拒绝写入当前患者数据。");
            }

            session.UpdatedAtUnixMs = nowUnixMs;
            context.AssessmentSessions.Update(session);
            await context.SaveChangesAsync(cancellationToken);
            return session;
        }

        session = new AssessmentSessionEntity
        {
            SessionKey = sessionKey,
            PatientCode = patientCode,
            StartedAtUnixMs = nowUnixMs,
            Status = "in_progress",
            UploadStatus = "local_only",
            CreatedAtUnixMs = nowUnixMs,
            UpdatedAtUnixMs = nowUnixMs
        };
        context.AssessmentSessions.Add(session);
        await context.SaveChangesAsync(cancellationToken);
        return session;
    }

    private async Task ExecuteWriteAsync(string databasePath, Func<Task> action, CancellationToken cancellationToken)
    {
        await ExecuteWriteAsync(databasePath, async () =>
        {
            await action();
            return true;
        }, cancellationToken);
    }

    private async Task<T> ExecuteWriteAsync<T>(string databasePath, Func<Task<T>> action, CancellationToken cancellationToken)
    {
        return await databaseWriteCoordinator.ExecuteAsync(databasePath, action, cancellationToken);
    }

    private static string ResolveModuleType(string moduleName)
    {
        return moduleName.Contains("问卷", StringComparison.Ordinal)
            || moduleName.Contains("个人基本信息", StringComparison.Ordinal)
                ? CaptureModuleTypes.Form
                : CaptureModuleTypes.Task;
    }

    public Task<int> RecoverIncompleteEegRecordingsAsync(CancellationToken cancellationToken = default)
    {
        var databasePath = AppDatabasePathProvider.MainDatabasePath;
        return ExecuteWriteAsync(databasePath, async () =>
        {
            await using var context = await OpenContextAsync(databasePath, cancellationToken);
            var recordings = await context.EegRecordings
                .Where(item => item.Status == "recording")
                .ToListAsync(cancellationToken);
            if (recordings.Count == 0)
            {
                return 0;
            }

            var now = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            var moduleRecordIds = recordings.Select(item => item.ModuleRecordId).Distinct().ToArray();
            foreach (var recording in recordings)
            {
                recording.Status = "interrupted";
                recording.EndedAtUnixMs = now;
            }

            var moduleRecords = await context.AssessmentModuleRecords
                .Where(item => moduleRecordIds.Contains(item.Id) && item.Status == "recording")
                .ToListAsync(cancellationToken);
            foreach (var moduleRecord in moduleRecords)
            {
                moduleRecord.Status = "interrupted";
                moduleRecord.EndedAtUnixMs = now;
            }

            await context.SaveChangesAsync(cancellationToken);
            return recordings.Count;
        }, cancellationToken);
    }
}
