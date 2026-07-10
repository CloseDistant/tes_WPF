namespace RuinaoHardwareDebugWpf;

using Microsoft.EntityFrameworkCore;
using System.Text.Json;

public sealed class LocalStimulationRecordService : IStimulationRecordService
{
    private readonly IPatientService patientService;
    private readonly IAppDatabaseInitializer databaseInitializer;
    private readonly IUnifiedSessionService unifiedSessionService;
    private readonly IAppDatabaseWriteCoordinator databaseWriteCoordinator;

    public LocalStimulationRecordService(
        IPatientService patientService,
        IAppDatabaseInitializer databaseInitializer,
        IUnifiedSessionService unifiedSessionService,
        IAppDatabaseWriteCoordinator databaseWriteCoordinator)
    {
        this.patientService = patientService;
        this.databaseInitializer = databaseInitializer;
        this.unifiedSessionService = unifiedSessionService;
        this.databaseWriteCoordinator = databaseWriteCoordinator;
    }

    public async Task RecordAsync(StimulationRecordRequest request, CancellationToken cancellationToken = default)
    {
        await databaseInitializer.EnsureInitializedAsync(cancellationToken);
        var session = await unifiedSessionService.GetOrStartAsync(cancellationToken);
        var timestamp = unifiedSessionService.GetCurrentTimestamp();
        var patientCode = await patientService.GetRequiredCurrentPatientCodeAsync(cancellationToken);
        var databasePath = AppDatabasePathProvider.MainDatabasePath;
        await databaseWriteCoordinator.ExecuteAsync(databasePath, async () =>
        {
            await using var context = new CaptureDbContext(databasePath);
            context.StimulationRecords.Add(new StimulationRecordEntity
            {
                PatientCode = patientCode,
                Action = request.Action,
                GroupTitle = request.GroupTitle,
                SelectedChannelNames = request.SelectedChannelNames,
                Status = request.Status,
                EventTimeUnixMs = timestamp.EventTimeUnixMs
            });
            await context.SaveChangesAsync(cancellationToken);
        }, cancellationToken);

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
}
