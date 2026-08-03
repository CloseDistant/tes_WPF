namespace RuinaoSoftwareWpf.Tests;

using Xunit;

public sealed class StimulationStateConfirmationTests
{
    [Fact]
    public async Task Start_RemainsStartingUntilHardwareConfirms()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var fixture = new Fixture();
        var operation = fixture.Engine.StartDirectCurrentGroupAsync(
            new TiGroup { Title = "tDCS" },
            "CH 1",
            "测试处方",
            cancellationToken);

        await fixture.Hardware.StartInvoked.Task.WaitAsync(TimeSpan.FromSeconds(1), cancellationToken);
        Assert.Equal(StimulationExecutionState.Starting, fixture.StateMachine.CurrentState);

        fixture.Hardware.StartCompletion.TrySetResult(
            new HardwareOperationResult(true, "confirmed"));
        await operation;

        Assert.Equal(StimulationExecutionState.Running, fixture.StateMachine.CurrentState);
    }

    [Fact]
    public async Task Start_MovesToFaultedWhenHardwareDoesNotConfirm()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var fixture = new Fixture();
        var operation = fixture.Engine.StartDirectCurrentGroupAsync(
            new TiGroup { Title = "tDCS" },
            "CH 1",
            "测试处方",
            cancellationToken);

        await fixture.Hardware.StartInvoked.Task.WaitAsync(TimeSpan.FromSeconds(1), cancellationToken);
        fixture.Hardware.StartCompletion.TrySetException(new TimeoutException("ACK timeout"));

        await Assert.ThrowsAsync<TimeoutException>(() => operation);
        Assert.Equal(StimulationExecutionState.Faulted, fixture.StateMachine.CurrentState);
    }

    [Fact]
    public async Task Stop_RemainsStoppingUntilHardwareConfirmsThenBecomesStopped()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var fixture = new Fixture();
        var operation = fixture.Engine.StopGroupAsync(
            new TiGroup { Title = "TI" },
            "CH 1",
            "TI",
            cancellationToken);

        await fixture.Hardware.StopInvoked.Task.WaitAsync(TimeSpan.FromSeconds(1), cancellationToken);
        Assert.Equal(StimulationExecutionState.Stopping, fixture.StateMachine.CurrentState);

        fixture.Hardware.StopCompletion.TrySetResult(
            new HardwareOperationResult(true, "confirmed"));
        await operation;

        Assert.Equal(StimulationExecutionState.Stopped, fixture.StateMachine.CurrentState);
    }

    [Fact]
    public async Task Complete_RemainsStoppingUntilHardwareConfirms()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var fixture = new Fixture();
        var operation = fixture.Engine.CompleteGroupAsync(
            new TiGroup { Title = "tDCS" },
            "CH 1",
            "tDCS",
            cancellationToken);

        await fixture.Hardware.CompleteInvoked.Task.WaitAsync(TimeSpan.FromSeconds(1), cancellationToken);
        Assert.Equal(StimulationExecutionState.Stopping, fixture.StateMachine.CurrentState);

        fixture.Hardware.CompleteCompletion.TrySetResult(
            new HardwareOperationResult(true, "confirmed"));
        await operation;

        Assert.Equal(StimulationExecutionState.Completed, fixture.StateMachine.CurrentState);
    }

    [Fact]
    public async Task CompleteOneOfMultipleActiveChannels_RemainsRunning()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var fixture = new Fixture();
        var group = new TiGroup { Title = "TI" };
        var first = new ChannelConfig { Name = "CH 1" };
        var second = new ChannelConfig { Name = "CH 2" };
        group.Channels.Add(first);
        group.Channels.Add(second);

        var startOperation = fixture.Engine.StartTiGroupAsync(
            group,
            "CH 1 + CH 2",
            "测试处方",
            cancellationToken);
        await fixture.Hardware.StartInvoked.Task.WaitAsync(TimeSpan.FromSeconds(1), cancellationToken);
        fixture.Hardware.StartCompletion.TrySetResult(new HardwareOperationResult(true, "confirmed"));
        await startOperation;

        var completedGroup = new TiGroup { Title = "TI" };
        completedGroup.Channels.Add(first);
        var completeOperation = fixture.Engine.CompleteGroupAsync(
            completedGroup,
            first.Name,
            "TI",
            cancellationToken);
        await fixture.Hardware.CompleteInvoked.Task.WaitAsync(TimeSpan.FromSeconds(1), cancellationToken);
        fixture.Hardware.CompleteCompletion.TrySetResult(new HardwareOperationResult(true, "confirmed"));
        await completeOperation;

        Assert.Equal(StimulationExecutionState.Running, fixture.StateMachine.CurrentState);
    }

    [Fact]
    public async Task EmergencyStop_RemainsStoppingUntilHardwareConfirmsThenBecomesTerminal()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var fixture = new Fixture();
        var operation = fixture.Engine.EmergencyStopTiGroupAsync(
            new TiGroup { Title = "TI" },
            "测试急停",
            cancellationToken);

        await fixture.Hardware.EmergencyStopInvoked.Task.WaitAsync(
            TimeSpan.FromSeconds(1),
            cancellationToken);
        Assert.Equal(StimulationExecutionState.Stopping, fixture.StateMachine.CurrentState);

        fixture.Hardware.EmergencyStopCompletion.TrySetResult(
            new HardwareOperationResult(true, "confirmed"));
        await operation;

        Assert.Equal(StimulationExecutionState.EmergencyStopped, fixture.StateMachine.CurrentState);
    }

    private sealed class Fixture
    {
        public Fixture()
        {
            var audit = new NullAuditLogService();
            StateMachine = new StimulationStateMachine(audit);
            Hardware = new ControlledHardwareService();
            Engine = new StimulationEngine(
                Hardware,
                new PassingSafetyService(),
                StateMachine,
                audit,
                new NoSessionService(),
                new SnapshotService(),
                new NoPatientService(),
                new SignedInAuthorizationService());
        }

        public ControlledHardwareService Hardware { get; }
        public StimulationStateMachine StateMachine { get; }
        public StimulationEngine Engine { get; }
    }

    private sealed class ControlledHardwareService : IHardwareService
    {
        public TaskCompletionSource StartInvoked { get; } = NewSignal();
        public TaskCompletionSource<HardwareOperationResult> StartCompletion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource StopInvoked { get; } = NewSignal();
        public TaskCompletionSource<HardwareOperationResult> StopCompletion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource EmergencyStopInvoked { get; } = NewSignal();
        public TaskCompletionSource<HardwareOperationResult> EmergencyStopCompletion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource CompleteInvoked { get; } = NewSignal();
        public TaskCompletionSource<HardwareOperationResult> CompleteCompletion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public event EventHandler<HardwareConnectionChangedEventArgs>? ConnectionChanged
        {
            add { }
            remove { }
        }
        public event EventHandler<DeviceTopologyChangedEventArgs>? DeviceTopologyChanged
        {
            add { }
            remove { }
        }
        public event EventHandler<StimulationImpedanceChangedEventArgs>? StimulationImpedanceChanged
        {
            add { }
            remove { }
        }
        public bool IsConnected => true;
        public bool IsConnecting => false;
        public DeviceTopologySnapshot? CurrentDeviceTopology => null;
        public StimulationImpedanceSnapshot? CurrentStimulationImpedance => null;

        public Task<HardwareOperationResult> StartGroupAsync(
            TiGroup group,
            string selectedChannelNames,
            PrescriptionDefinition parameterRecord,
            CancellationToken cancellationToken = default)
        {
            StartInvoked.TrySetResult();
            return StartCompletion.Task.WaitAsync(cancellationToken);
        }

        public Task<HardwareOperationResult> ConnectAsync(CancellationToken cancellationToken = default) => NotUsed();
        public Task<HardwareOperationResult> HandshakeAsync(CancellationToken cancellationToken = default) => NotUsed();
        public Task<HardwareOperationResult> DisconnectAsync(CancellationToken cancellationToken = default) => NotUsed();
        public Task<HardwareOperationResult> ReadProductModelAsync(CancellationToken cancellationToken = default) => NotUsed();
        public Task<HardwareOperationResult> ReadBoardModelAsync(CancellationToken cancellationToken = default) => NotUsed();
        public Task<HardwareOperationResult> CheckImpedanceAsync(CancellationToken cancellationToken = default) => NotUsed();
        public void SetStimulationImpedanceMonitoringEnabled(bool enabled)
        {
        }
        public Task<DeviceTopologySnapshot> RefreshDeviceTopologyAsync(CancellationToken cancellationToken = default) =>
            Task.FromException<DeviceTopologySnapshot>(new NotSupportedException());
        public Task<HardwareOperationResult> StopGroupAsync(
            TiGroup group,
            string selectedChannelNames,
            string stimulationType,
            CancellationToken cancellationToken = default)
        {
            StopInvoked.TrySetResult();
            return StopCompletion.Task.WaitAsync(cancellationToken);
        }
        public Task<HardwareOperationResult> EmergencyStopGroupAsync(
            TiGroup group,
            string selectedChannelNames,
            string stimulationType = "TI",
            CancellationToken cancellationToken = default)
        {
            EmergencyStopInvoked.TrySetResult();
            return EmergencyStopCompletion.Task.WaitAsync(cancellationToken);
        }
        public Task<HardwareOperationResult> CompleteGroupAsync(TiGroup group, string selectedChannelNames, string stimulationType, CancellationToken cancellationToken = default)
        {
            CompleteInvoked.TrySetResult();
            return CompleteCompletion.Task.WaitAsync(cancellationToken);
        }
        public Task ShutdownAsync() => Task.CompletedTask;

        private static Task<HardwareOperationResult> NotUsed() =>
            Task.FromException<HardwareOperationResult>(new NotSupportedException());
    }

    private sealed class PassingSafetyService : ISafetyService
    {
        public Task<SafetyEvaluationResult> EvaluateAsync(SensorSnapshot snapshot, CancellationToken cancellationToken = default) =>
            Task.FromResult(new SafetyEvaluationResult(SafetyAction.None, string.Empty, DateTimeOffset.UtcNow));
        public Task EnsureCanStartStimulationAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class SignedInAuthorizationService : IAuthorizationService
    {
        private static readonly CurrentUserInfo User = new(1, "Doctor01", "Doctor01", AccountRoles.Doctor, false);
        public CurrentUserInfo RequireSignedIn() => User;
        public bool HasPermission(AppPermission permission) => true;
        public CurrentUserInfo Demand(AppPermission permission) => User;
    }

    private sealed class NoPatientService : IPatientService
    {
        public event EventHandler? CurrentPatientChanged
        {
            add { }
            remove { }
        }
        public PatientRecord? CurrentPatient => null;
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<string> GenerateNextPatientCodeAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PatientRecord> CreatePatientAsync(PatientSaveRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PatientRecord> UpdatePatientAsync(PatientSaveRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PageResult<PatientRecord>> GetPatientsPageAsync(PageRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PatientRecord> SwitchCurrentPatientAsync(string patientCode, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string> GetRequiredCurrentPatientCodeAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class NoSessionService : IUnifiedSessionService
    {
        public event EventHandler? CurrentSessionChanged
        {
            add { }
            remove { }
        }
        public UnifiedSessionContext? CurrentSession => null;
        public Task<UnifiedSessionContext> GetOrStartAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public UnifiedSessionTimestamp GetCurrentTimestamp() => throw new NotSupportedException();
        public Task<PageResult<UnifiedSessionTimelineEvent>> GetTimelinePageAsync(string sessionKey, PageRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RecordEventAsync(string moduleCode, string eventType, string? message = null, string? payloadJson = null, DateTimeOffset? sourceTime = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> EndAsync(
            string status,
            string? reason = null,
            string? expectedSessionKey = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }

    private sealed class SnapshotService : IRunConfigurationSnapshotService
    {
        public RunConfigurationSnapshot Capture<T>(string sessionKey, string moduleCode, T configuration) =>
            new(sessionKey, moduleCode, 1, DateTimeOffset.UtcNow, string.Empty);
        public RunConfigurationSnapshot? GetCurrent(string moduleCode) => null;
        public void Clear(string moduleCode) { }
    }

    private sealed class NullAuditLogService : IAuditLogService
    {
        public void RecordStateTransition<TState>(StateTransition<TState> transition) { }
        public void RecordUserAction(string action, string operatorId = "system") { }
        public void RecordHardwareCommunication(string direction, string command, string details) { }
        public void RecordSafetyEvent(SafetyEvaluationResult result, string operatorId = "system") { }
    }

    private static TaskCompletionSource NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
