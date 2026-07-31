namespace RuinaoSoftwareWpf.Tests;

using RuinaoSoftwareWpf.ApplicationContracts;
using Xunit;

public sealed class AssessmentModuleLifecycleServiceTests
{
    [Fact]
    public async Task StartAsync_RejectsAttemptForDifferentCurrentPatient()
    {
        var store = new RecordingAssessmentRunStore();
        var service = new AssessmentModuleLifecycleService(
            store,
            new FixedPatientService(CreatePatient("patient-a")),
            TimeProvider.System);
        var request = new AssessmentModuleStartRequest(
            "patient-b",
            "session-1",
            "eye_calibration",
            "眼动校准",
            0,
            22);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.StartAsync(request, TestContext.Current.CancellationToken));

        Assert.Equal(0, store.StartCount);
    }

    [Fact]
    public async Task StartAsync_ForwardsPatientScopedAttemptToStore()
    {
        var store = new RecordingAssessmentRunStore();
        var service = new AssessmentModuleLifecycleService(
            store,
            new FixedPatientService(CreatePatient("patient-a")),
            TimeProvider.System);
        var request = new AssessmentModuleStartRequest(
            "patient-a",
            "session-1",
            "eye_calibration",
            "眼动校准",
            0,
            22);

        var result = await service.StartAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(1, store.StartCount);
        Assert.Equal("patient-a", result.PatientCode);
        Assert.Equal("session-1", result.SessionKey);
        Assert.Equal(41, result.AttemptId);
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

    private sealed class RecordingAssessmentRunStore : IAssessmentRunStore
    {
        public int StartCount { get; private set; }

        public Task<AssessmentProgressSnapshot> GetProgressAsync(
            string patientCode,
            int totalModuleCount,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new AssessmentProgressSnapshot(
                1,
                patientCode,
                AssessmentRunStatus.InProgress,
                0,
                []));

        public Task<AssessmentModuleRunContext> StartModuleAsync(
            AssessmentModuleStartRequest request,
            DateTimeOffset startedAt,
            CancellationToken cancellationToken = default)
        {
            StartCount++;
            return Task.FromResult(new AssessmentModuleRunContext(
                7,
                41,
                1,
                request.PatientCode,
                request.SessionKey,
                request.ModuleCode,
                request.ModuleName,
                request.ModuleIndex,
                startedAt));
        }

        public Task MarkSavingAsync(
            long attemptId,
            DateTimeOffset updatedAt,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<AssessmentModuleResult> CompleteModuleAsync(
            long attemptId,
            string? resultJson,
            DateTimeOffset endedAt,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateResult(attemptId, AssessmentModuleExecutionStatus.Completed, endedAt));

        public Task<AssessmentModuleResult> CancelModuleAsync(
            long attemptId,
            string reason,
            DateTimeOffset endedAt,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateResult(attemptId, AssessmentModuleExecutionStatus.CancelledInvalid, endedAt));

        public Task<AssessmentModuleResult> FailModuleAsync(
            long attemptId,
            string errorCode,
            string message,
            DateTimeOffset endedAt,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateResult(attemptId, AssessmentModuleExecutionStatus.Failed, endedAt));

        private static AssessmentModuleResult CreateResult(
            long attemptId,
            AssessmentModuleExecutionStatus status,
            DateTimeOffset endedAt) =>
            new(7, attemptId, status, endedAt, endedAt, null, null);
    }

    private sealed class FixedPatientService : IPatientService
    {
        public FixedPatientService(PatientRecord patient)
        {
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
            Task.FromResult(CurrentPatient!.PatientCode);
    }
}
