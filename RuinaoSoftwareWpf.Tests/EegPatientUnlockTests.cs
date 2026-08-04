namespace RuinaoSoftwareWpf.Tests;

using System.ComponentModel;
using Xunit;

public sealed class EegPatientUnlockTests
{
    [Fact]
    public async Task Stop_KeepsRecordingActiveUntilFinalizationThenNotifiesUnlock()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var acquisition = new ControlledAcquisitionService();
        var recording = new ControlledRecordingService();
        var viewModel = new EegSignalCaptureViewModel(
            acquisition,
            recording,
            new NoSessionService(),
            new NoDialogService(),
            new NoPatientService());
        var stoppedNotification = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(EegSignalCaptureViewModel.IsRecording)
                && !viewModel.IsRecording)
            {
                stoppedNotification.TrySetResult();
            }
        };

        var stopOperation = viewModel.StopAsync(cancellationToken);
        await recording.StopInvoked.Task.WaitAsync(TimeSpan.FromSeconds(1), cancellationToken);

        Assert.Equal(EegAcquisitionState.Stopped, acquisition.State);
        Assert.True(viewModel.IsRecording);

        recording.AllowStopToComplete.TrySetResult();
        await stopOperation;
        await stoppedNotification.Task.WaitAsync(TimeSpan.FromSeconds(1), cancellationToken);

        Assert.False(viewModel.IsRecording);
    }

    private sealed class ControlledAcquisitionService : ILegacyEegAcquisitionService
    {
        public EegAcquisitionState State { get; private set; } = EegAcquisitionState.Acquiring;

        public EegAcquisitionConfig Config { get; private set; } = new();

        public IReadOnlyList<EegMarkerTag> MarkerTags => [];

        public event EventHandler<EegAcquisitionState>? StateChanged;
        public event EventHandler<EegWaveformRenderModel>? RenderModelUpdated
        {
            add { }
            remove { }
        }

        public event EventHandler<IReadOnlyList<EegMarkerRecord>>? MarkersChanged
        {
            add { }
            remove { }
        }

        public event EventHandler<EegSampleBatch>? SamplesGenerated
        {
            add { }
            remove { }
        }

        public void Configure(EegAcquisitionConfig config) => Config = config;

        public Task StartAsync(string recordName, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            State = EegAcquisitionState.Stopped;
            StateChanged?.Invoke(this, State);
            return Task.CompletedTask;
        }

        public void AddMarker(EegMarkerTag tag, string source)
        {
        }

        public void ReplaceMarkerTags(IReadOnlyList<EegMarkerTag> markerTags)
        {
        }

        public IReadOnlyList<EegMarkerRecord> GetMarkers() => [];

        public EegWaveformRenderModel GetCurrentRenderModel() =>
            new(
                Config,
                [],
                0,
                0,
                0,
                TimeSpan.Zero,
                false,
                [],
                []);
    }

    private sealed class ControlledRecordingService : IEegRecordingService
    {
        public TaskCompletionSource StopInvoked { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource AllowStopToComplete { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool IsRecording { get; private set; } = true;

        public Task StartAsync(
            string recordName,
            EegAcquisitionConfig config,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public bool TryAppendSamples(EegSampleBatch batch) => false;

        public Task AppendSamplesAsync(
            EegSampleBatch batch,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task AddMarkerAsync(
            EegMarkerRecord marker,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public async Task StopAsync(
            string status = "completed",
            CancellationToken cancellationToken = default)
        {
            StopInvoked.TrySetResult();
            await AllowStopToComplete.Task.WaitAsync(cancellationToken);
            IsRecording = false;
        }
    }

    private sealed class NoSessionService : IUnifiedSessionService
    {
        public event EventHandler? CurrentSessionChanged
        {
            add { }
            remove { }
        }

        public UnifiedSessionContext? CurrentSession => null;

        public Task<UnifiedSessionContext> GetOrStartAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public UnifiedSessionTimestamp GetCurrentTimestamp() =>
            throw new NotSupportedException();

        public Task<PageResult<UnifiedSessionTimelineEvent>> GetTimelinePageAsync(
            string sessionKey,
            PageRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task RecordEventAsync(
            string moduleCode,
            string eventType,
            string? message = null,
            string? payloadJson = null,
            DateTimeOffset? sourceTime = null,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<bool> EndAsync(
            string status,
            string? reason = null,
            string? expectedSessionKey = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }

    private sealed class NoDialogService : IUserDialogService
    {
        public bool ConfirmWarning(
            string title,
            string message,
            string confirmText,
            string cancelText) =>
            false;

        public bool ConfirmDirectCurrentStart(DirectCurrentStartConfirmationRequest request) =>
            false;

        public void ShowInformation(string title, string message)
        {
        }

        public void ShowError(string title, string message)
        {
        }

        public Task<PrescriptionDefinition?> SelectStimulationPrescriptionAsync(
            string stimulationType,
            string applyScopeText,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<PrescriptionDefinition?>(null);
    }

    private sealed class NoPatientService : IPatientService
    {
        public event EventHandler? CurrentPatientChanged
        {
            add { }
            remove { }
        }

        public PatientRecord? CurrentPatient => null;

        public Task InitializeAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<string> GenerateNextPatientCodeAsync(
            CancellationToken cancellationToken = default) =>
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
            throw new NotSupportedException();
    }
}
