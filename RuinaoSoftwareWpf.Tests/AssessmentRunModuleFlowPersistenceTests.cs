namespace RuinaoSoftwareWpf.Tests;

using Microsoft.EntityFrameworkCore;
using RuinaoSoftwareWpf.ApplicationContracts;
using Xunit;

[Collection(DatabaseEnvironmentCollection.Name)]
public sealed class AssessmentRunModuleFlowPersistenceTests
{
    private static readonly AssessmentFlowModuleDefinition Picture =
        new(AssessmentModuleTypeIds.PictureBrowse, "picture_browse");

    private static readonly AssessmentFlowModuleDefinition Video =
        new(AssessmentModuleTypeIds.VideoBrowse, "video_browse");

    private static readonly AssessmentFlowModuleDefinition Voice =
        new(AssessmentModuleTypeIds.VoiceBaseline, "voice_baseline");

    [Fact]
    public Task ExistingRun_PreservesSnapshotWhenCurrentFlowIsAddedAndReordered() =>
        RunInIsolatedDataDirectoryAsync(async (repository, databasePath, cancellationToken) =>
        {
            var run = await repository.CreateRunAsync(
                "patient-a",
                [Picture, Video],
                DateTimeOffset.UtcNow,
                cancellationToken);

            var loaded = await repository.GetActiveRunAsync(
                "patient-a",
                [Voice, Video, Picture],
                cancellationToken);

            Assert.NotNull(loaded);
            Assert.Equal(AssessmentModuleTypeIds.PictureBrowse, loaded.NextModuleTypeId);
            Assert.Equal(
                [AssessmentModuleTypeIds.PictureBrowse, AssessmentModuleTypeIds.VideoBrowse],
                loaded.ModuleFlow.Select(static item => item.ModuleTypeId));
            Assert.Equal([0, 1], loaded.ModuleFlow.Select(static item => item.Sequence));

            await using var context = new CaptureDbContext(databasePath);
            Assert.Equal(2, await context.AssessmentRunModules.CountAsync(
                item => item.RunId == run.RunId,
                cancellationToken));
            Assert.False(await context.AssessmentRunModules.AnyAsync(
                item => item.RunId == run.RunId
                    && item.ModuleTypeId == AssessmentModuleTypeIds.VoiceBaseline,
                cancellationToken));
        });

    [Fact]
    public Task ExistingRun_SkipsRemovedPendingModuleAndContinuesByStableTypeId() =>
        RunInIsolatedDataDirectoryAsync(async (repository, databasePath, cancellationToken) =>
        {
            var run = await repository.CreateRunAsync(
                "patient-a",
                [Picture, Video, Voice],
                DateTimeOffset.UtcNow,
                cancellationToken);
            var attempt = await repository.StartModuleAsync(
                new AssessmentModuleStartRequest(
                    run.RunId,
                    "patient-a",
                    "session-picture",
                    Picture.ModuleCode,
                    "图片浏览",
                    Picture.ModuleTypeId,
                    0,
                    3),
                DateTimeOffset.UtcNow,
                cancellationToken);
            await repository.CompleteModuleAsync(
                attempt.AttemptId,
                null,
                DateTimeOffset.UtcNow,
                cancellationToken);

            var loaded = await repository.GetActiveRunAsync(
                "patient-a",
                [Picture, Voice],
                cancellationToken);

            Assert.NotNull(loaded);
            Assert.Equal(AssessmentModuleTypeIds.VoiceBaseline, loaded.NextModuleTypeId);
            Assert.Equal(
                [AssessmentModuleTypeIds.PictureBrowse, AssessmentModuleTypeIds.VoiceBaseline],
                loaded.ModuleFlow.Select(static item => item.ModuleTypeId));
            Assert.Equal([0, 2], loaded.ModuleFlow.Select(static item => item.Sequence));

            await using var context = new CaptureDbContext(databasePath);
            var removed = await context.AssessmentRunModules.SingleAsync(
                item => item.RunId == run.RunId
                    && item.ModuleTypeId == AssessmentModuleTypeIds.VideoBrowse,
                cancellationToken);
            Assert.Equal("skipped_removed", removed.Status);
        });

    [Fact]
    public Task LegacyRunWithoutSnapshot_CreatesOneSnapshotAtFirstRead() =>
        RunInIsolatedDataDirectoryAsync(async (repository, databasePath, cancellationToken) =>
        {
            await using (var context = new CaptureDbContext(databasePath))
            {
                context.AssessmentRuns.Add(new AssessmentRunEntity
                {
                    PatientCode = "patient-a",
                    Status = "in_progress",
                    TotalModuleCount = 2,
                    NextModuleIndex = 1,
                    StartedAtUnixMs = 1,
                    CreatedAtUnixMs = 1,
                    UpdatedAtUnixMs = 1
                });
                await context.SaveChangesAsync(cancellationToken);
            }

            var loaded = await repository.GetActiveRunAsync(
                "patient-a",
                [Picture, Video],
                cancellationToken);

            Assert.NotNull(loaded);
            Assert.Equal(AssessmentModuleTypeIds.VideoBrowse, loaded.NextModuleTypeId);
            Assert.Equal(["completed", "pending"], loaded.ModuleFlow.Select(static item => item.Status));

            await using var verification = new CaptureDbContext(databasePath);
            Assert.Equal(2, await verification.AssessmentRunModules.CountAsync(cancellationToken));
        });

    private static async Task RunInIsolatedDataDirectoryAsync(
        Func<SqliteCaptureRecordingRepository, string, CancellationToken, Task> test)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"ruinao-assessment-flow-{Guid.NewGuid():N}");
        var previousDirectory = Environment.GetEnvironmentVariable("RUINAO_DATA_DIRECTORY");
        Environment.SetEnvironmentVariable("RUINAO_DATA_DIRECTORY", directory);
        try
        {
            var patient = CreatePatient("patient-a");
            var logger = new TestLoggingService();
            var initializer = new AppDatabaseInitializer(logger);
            await initializer.EnsureInitializedAsync(cancellationToken);
            var repository = new SqliteCaptureRecordingRepository(
                logger,
                new FixedPatientService(patient),
                initializer,
                new InlineDatabaseWriteCoordinator());
            await test(
                repository,
                Path.Combine(directory, "ruinao_app.db"),
                cancellationToken);
        }
        finally
        {
            Environment.SetEnvironmentVariable("RUINAO_DATA_DIRECTORY", previousDirectory);
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static PatientRecord CreatePatient(string patientCode) =>
        new(
            patientCode,
            "测试患者",
            PatientSex.Male,
            new DateOnly(1990, 1, 1),
            36,
            null,
            string.Empty,
            null,
            null,
            null,
            null,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

    private sealed class FixedPatientService : IPatientService
    {
        private readonly PatientRecord patient;

        public FixedPatientService(PatientRecord patient)
        {
            this.patient = patient;
            CurrentPatient = patient;
        }

        public event EventHandler? CurrentPatientChanged
        {
            add { }
            remove { }
        }

        public PatientRecord? CurrentPatient { get; }

        public Task InitializeAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<string> GenerateNextPatientCodeAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PatientRecord> CreatePatientAsync(
            PatientSaveRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PatientRecord> UpdatePatientAsync(
            PatientSaveRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PageResult<PatientRecord>> GetPatientsPageAsync(
            PageRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PatientRecord> SwitchCurrentPatientAsync(
            string patientCode,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<string> GetRequiredCurrentPatientCodeAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(patient.PatientCode);
    }

    private sealed class InlineDatabaseWriteCoordinator : IAppDatabaseWriteCoordinator
    {
        public Task ExecuteAsync(
            string databasePath,
            Func<Task> operation,
            CancellationToken cancellationToken = default) =>
            operation();

        public Task<T> ExecuteAsync<T>(
            string databasePath,
            Func<Task<T>> operation,
            CancellationToken cancellationToken = default) =>
            operation();
    }

    private sealed class TestLoggingService : ILoggingService
    {
        public string CurrentLogPath => string.Empty;
        public void Debug(string message) { }
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message, Exception? exception = null) { }
        public void Hardware(string message) { }
        public void HardwareTx(string command, byte[] frame) { }
        public void HardwareRx(string source, byte[] frame) { }
        public void HardwareDecision(string message) { }
    }
}
