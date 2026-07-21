namespace RuinaoSoftwareWpf;

using Microsoft.EntityFrameworkCore;
using System.Text.Json;

public sealed class LocalStimulationRecordService : IStimulationRecordService
{
    private const string DefaultAdverseReactionRecord = "无不良反应记录";

    private readonly IPatientService patientService;
    private readonly IAppDatabaseInitializer databaseInitializer;
    private readonly IUnifiedSessionService unifiedSessionService;
    private readonly IAppDatabaseWriteCoordinator databaseWriteCoordinator;
    private readonly IAuthorizationService authorizationService;

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

    public async Task RecordAsync(StimulationRecordRequest request, CancellationToken cancellationToken = default)
    {
        var currentUser = authorizationService.RequireSignedIn();
        await databaseInitializer.EnsureInitializedAsync(cancellationToken);
        var patientCode = patientService.CurrentPatient?.PatientCode;
        UnifiedSessionContext? session = null;
        var eventTimeUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (!string.IsNullOrWhiteSpace(patientCode))
        {
            session = await unifiedSessionService.GetOrStartAsync(cancellationToken);
            eventTimeUnixMs = unifiedSessionService.GetCurrentTimestamp().EventTimeUnixMs;
        }

        var databasePath = AppDatabasePathProvider.MainDatabasePath;
        await databaseWriteCoordinator.ExecuteAsync(databasePath, async () =>
        {
            await using var context = new CaptureDbContext(databasePath);
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            var entity = new StimulationRecordEntity
            {
                OperatorUserId = currentUser.UserId,
                PatientCode = patientCode,
                Action = request.Action,
                GroupTitle = request.GroupTitle,
                SelectedChannelNames = request.SelectedChannelNames,
                Status = request.Status,
                StimulationType = request.StimulationType,
                PrescriptionName = request.PrescriptionName,
                AdverseReactionRecord = string.IsNullOrWhiteSpace(request.AdverseReactionRecord)
                    ? DefaultAdverseReactionRecord
                    : request.AdverseReactionRecord,
                ParameterSnapshotJson = request.ParameterSnapshotJson,
                EventTimeUnixMs = eventTimeUnixMs
            };
            context.StimulationRecords.Add(entity);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }, cancellationToken);

        if (session is null)
        {
            return;
        }

        var payloadJson = JsonSerializer.Serialize(new
        {
            session.SessionKey,
            request.Action,
            request.GroupTitle,
            request.SelectedChannelNames,
            request.Status
        });
        await unifiedSessionService.RecordEventAsync(
            SessionModuleCodes.Stimulation,
            request.Action,
            $"{request.GroupTitle}：{request.Status}",
            payloadJson,
            cancellationToken: cancellationToken);
    }

    public async Task<PageResult<StimulationTreatmentRecord>> GetTreatmentRecordsPageAsync(
        PageRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var currentUser = authorizationService.RequireSignedIn();
        await databaseInitializer.EnsureInitializedAsync(cancellationToken);
        await using var context = new CaptureDbContext(AppDatabasePathProvider.MainDatabasePath);
        var query = context.StimulationRecords
            .AsNoTracking()
            .Where(item => item.Action == "start" && item.OperatorUserId == currentUser.UserId);
        var totalCount = await query.CountAsync(cancellationToken);
        var records = await query
            .OrderByDescending(item => item.EventTimeUnixMs)
            .ThenByDescending(item => item.Id)
            .Skip(request.SafeOffset)
            .Take(request.SafePageSize + 1)
            .ToListAsync(cancellationToken);
        var hasMore = records.Count > request.SafePageSize;
        if (hasMore)
        {
            records.RemoveAt(records.Count - 1);
        }
        foreach (var record in records)
        {
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
        foreach (var patient in patientEntities)
        {
        }
        var patients = patientEntities.ToDictionary(
            item => item.PatientCode,
            item => item.Name ?? item.PatientCode,
            StringComparer.Ordinal);

        var items = records.Select(item =>
        {
            var parameterRecord = StimulationRecordParameters.FromJson(item.ParameterSnapshotJson)
                ?? StimulationRecordParameters.CreateFallbackRecord(
                    item.Id,
                    item.GroupTitle,
                    item.SelectedChannelNames,
                    item.StimulationType,
                    item.PrescriptionName);

            return new StimulationTreatmentRecord(
                item.Id,
                GetPatientDisplay(item.PatientCode, patients),
                string.IsNullOrWhiteSpace(item.StimulationType) ? parameterRecord.StimulationType : item.StimulationType,
                DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeMilliseconds(item.EventTimeUnixMs).LocalDateTime),
                string.IsNullOrWhiteSpace(item.PrescriptionName) ? parameterRecord.Name : item.PrescriptionName,
                string.IsNullOrWhiteSpace(item.AdverseReactionRecord) ? DefaultAdverseReactionRecord : item.AdverseReactionRecord,
                parameterRecord);
        }).ToArray();
        return new PageResult<StimulationTreatmentRecord>(items, hasMore, totalCount);
    }

    private static string GetPatientDisplay(string? patientCode, IReadOnlyDictionary<string, string> patients)
    {
        if (string.IsNullOrWhiteSpace(patientCode))
        {
            return "null";
        }

        return patients.TryGetValue(patientCode, out var name) && !string.IsNullOrWhiteSpace(name)
            ? name
            : patientCode;
    }
}
