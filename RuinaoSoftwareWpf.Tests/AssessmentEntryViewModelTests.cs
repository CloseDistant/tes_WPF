namespace RuinaoSoftwareWpf.Tests;

using RuinaoSoftwareWpf.ApplicationContracts;
using Xunit;

public sealed class AssessmentEntryViewModelTests
{
    [Fact]
    public async Task LoadAsync_WithoutPatient_ShowsPatientSelectionStateWithoutQueryingRun()
    {
        var coordinator = new RecordingRunCoordinator();
        var patientService = new FixedPatientService(null);
        var viewModel = CreateViewModel(coordinator, patientService);

        await viewModel.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(AssessmentEntryState.NoPatient, viewModel.State);
        Assert.True(viewModel.IsNoPatientState);
        Assert.Equal("选择患者", viewModel.SelectPatientActionText);
        Assert.Equal(0, coordinator.GetActiveCount);
    }

    [Fact]
    public async Task MatchPatientAsync_RaisesMatchingRequest()
    {
        var coordinator = new RecordingRunCoordinator();
        var patientService = new FixedPatientService(null);
        var viewModel = CreateViewModel(coordinator, patientService);
        var raised = false;
        viewModel.PatientMatchingRequested += (_, request) =>
        {
            raised = true;
            request.IsHandled = true;
        };

        await viewModel.LoadAsync(TestContext.Current.CancellationToken);
        await viewModel.ExecuteMatchPatientAsync(TestContext.Current.CancellationToken);

        Assert.True(raised);
    }

    [Fact]
    public async Task SelectPatientAsync_AfterSelection_ReloadsAssessmentEntry()
    {
        var coordinator = new RecordingRunCoordinator();
        var patientService = new FixedPatientService(null);
        var viewModel = CreateViewModel(coordinator, patientService);
        viewModel.PatientSelectionRequested += (_, request) =>
        {
            request.IsHandled = true;
            patientService.SetCurrentPatient(CreatePatient("patient-a"));
        };

        await viewModel.LoadAsync(TestContext.Current.CancellationToken);
        await viewModel.ExecuteSelectPatientAsync(TestContext.Current.CancellationToken);

        Assert.Equal(AssessmentEntryState.NoActiveRun, viewModel.State);
        Assert.False(viewModel.IsNoPatientState);
        Assert.Equal(1, coordinator.GetActiveCount);
    }

    [Fact]
    public async Task LoadAsync_WithoutActiveRun_ShowsStartNewAssessment()
    {
        var coordinator = new RecordingRunCoordinator();
        var viewModel = CreateViewModel(coordinator);

        await viewModel.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(AssessmentEntryState.NoActiveRun, viewModel.State);
        Assert.Equal("开始新的评估", viewModel.PrimaryActionText);
    }

    [Fact]
    public async Task LoadAndContinue_UsesTheSameActiveRunId()
    {
        var existing = new AssessmentRunContext(
            37,
            "patient-a",
            10,
            AssessmentCaptureViewModel.TotalFormalModuleCount,
            DateTimeOffset.UtcNow);
        var coordinator = new RecordingRunCoordinator { ActiveRun = existing };
        var viewModel = CreateViewModel(coordinator);
        AssessmentRunContext? activated = null;
        viewModel.RunActivated += (_, run) => activated = run;

        await viewModel.LoadAsync(TestContext.Current.CancellationToken);
        await viewModel.ExecutePrimaryActionAsync(TestContext.Current.CancellationToken);

        Assert.Equal("继续评估", viewModel.PrimaryActionText);
        Assert.Equal(37, coordinator.ResumedRunId);
        Assert.Same(existing, activated);
    }

    [Fact]
    public async Task StartNew_CreatesRunBeforeOpeningWorkbench()
    {
        var created = new AssessmentRunContext(
            41,
            "patient-a",
            0,
            AssessmentCaptureViewModel.TotalFormalModuleCount,
            DateTimeOffset.UtcNow);
        var coordinator = new RecordingRunCoordinator { CreatedRun = created };
        var viewModel = CreateViewModel(coordinator);
        AssessmentRunContext? activated = null;
        viewModel.RunActivated += (_, run) => activated = run;

        await viewModel.LoadAsync(TestContext.Current.CancellationToken);
        await viewModel.ExecutePrimaryActionAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, coordinator.CreateCount);
        Assert.Same(created, activated);
    }

    private static AssessmentEntryViewModel CreateViewModel(RecordingRunCoordinator coordinator) =>
        CreateViewModel(coordinator, new FixedPatientService(CreatePatient("patient-a")));

    private static AssessmentEntryViewModel CreateViewModel(
        RecordingRunCoordinator coordinator,
        FixedPatientService patientService) =>
        new(
            coordinator,
            patientService,
            new AppLocalizationService(),
            new NullLoggingService());

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

    private sealed class RecordingRunCoordinator : IAssessmentRunCoordinator
    {
        public AssessmentRunContext? ActiveRun { get; init; }
        public AssessmentRunContext? CreatedRun { get; init; }
        public long? ResumedRunId { get; private set; }
        public int CreateCount { get; private set; }
        public int GetActiveCount { get; private set; }

        public Task<AssessmentRunContext?> GetActiveRunAsync(
            int totalModuleCount,
            CancellationToken cancellationToken = default)
        {
            GetActiveCount++;
            return Task.FromResult(ActiveRun);
        }

        public Task<AssessmentRunContext> CreateRunAsync(
            int totalModuleCount,
            CancellationToken cancellationToken = default)
        {
            CreateCount++;
            return Task.FromResult(CreatedRun ?? new AssessmentRunContext(
                1,
                "patient-a",
                0,
                totalModuleCount,
                DateTimeOffset.UtcNow));
        }

        public Task<AssessmentRunContext> ResumeRunAsync(
            long runId,
            int totalModuleCount,
            CancellationToken cancellationToken = default)
        {
            ResumedRunId = runId;
            return Task.FromResult(ActiveRun
                ?? throw new InvalidOperationException("测试未配置活动 Run。"));
        }
    }

    private sealed class FixedPatientService(PatientRecord? patient) : IPatientService
    {
        public event EventHandler? CurrentPatientChanged;

        public PatientRecord? CurrentPatient { get; private set; } = patient;

        public void SetCurrentPatient(PatientRecord currentPatient)
        {
            CurrentPatient = currentPatient;
            CurrentPatientChanged?.Invoke(this, EventArgs.Empty);
        }
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<string> GenerateNextPatientCodeAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PatientRecord> CreatePatientAsync(PatientSaveRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PatientRecord> UpdatePatientAsync(PatientSaveRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PageResult<PatientRecord>> GetPatientsPageAsync(PageRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PatientRecord> SwitchCurrentPatientAsync(string patientCode, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string> GetRequiredCurrentPatientCodeAsync(CancellationToken cancellationToken = default) => Task.FromResult(CurrentPatient!.PatientCode);
    }

    private sealed class NullLoggingService : ILoggingService
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
