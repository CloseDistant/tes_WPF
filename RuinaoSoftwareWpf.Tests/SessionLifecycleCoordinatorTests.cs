namespace RuinaoSoftwareWpf.Tests;

using RuinaoSoftwareWpf.ApplicationContracts;
using Xunit;

public sealed class SessionLifecycleCoordinatorTests
{
    [Fact]
    public async Task EndCurrentAsync_WhenSessionChangesAfterConfirmation_DoesNotEndNewSession()
    {
        var sessions = new ControlledUnifiedSessionService(CreateSession("session-a"));
        var coordinator = CreateCoordinator(sessions);
        var cancellationToken = TestContext.Current.CancellationToken;

        var confirmationResult = await coordinator.EndCurrentAsync(
            cancellationToken: cancellationToken);
        var confirmation = Assert.IsType<SessionLifecycleConfirmationRequest>(
            confirmationResult.Confirmation);
        sessions.CurrentSession = CreateSession("session-b");

        var result = await coordinator.EndCurrentAsync(
            confirmation.SessionKey,
            cancellationToken);

        Assert.False(result.Succeeded);
        Assert.Contains("发生变化", result.Message, StringComparison.Ordinal);
        Assert.Null(sessions.LastExpectedSessionKey);
        Assert.Equal("session-b", sessions.CurrentSession?.SessionKey);
    }

    [Fact]
    public async Task EndCurrentAsync_WhenSessionMatchesConfirmation_EndsExpectedSession()
    {
        var sessions = new ControlledUnifiedSessionService(CreateSession("session-a"));
        var coordinator = CreateCoordinator(sessions);
        var cancellationToken = TestContext.Current.CancellationToken;
        var confirmationResult = await coordinator.EndCurrentAsync(
            cancellationToken: cancellationToken);
        var confirmation = Assert.IsType<SessionLifecycleConfirmationRequest>(
            confirmationResult.Confirmation);

        var result = await coordinator.EndCurrentAsync(
            confirmation.SessionKey,
            cancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal("session-a", sessions.LastExpectedSessionKey);
        Assert.Null(sessions.CurrentSession);
    }

    private static SessionLifecycleCoordinator CreateCoordinator(
        ControlledUnifiedSessionService sessions)
    {
        return new SessionLifecycleCoordinator(
            sessions,
            new IdleStimulationStateMachine(),
            new IdleEegRecordingService(),
            new IdleCaptureMediaService());
    }

    private static UnifiedSessionContext CreateSession(string sessionKey)
    {
        return new UnifiedSessionContext(
            sessionKey,
            "patient-001",
            DateTimeOffset.UtcNow,
            0,
            TimeSpan.TicksPerSecond);
    }

    private sealed class ControlledUnifiedSessionService : IUnifiedSessionService
    {
        public ControlledUnifiedSessionService(UnifiedSessionContext currentSession)
        {
            CurrentSession = currentSession;
        }

        public event EventHandler? CurrentSessionChanged;

        public UnifiedSessionContext? CurrentSession { get; set; }

        public string? LastExpectedSessionKey { get; private set; }

        public Task<UnifiedSessionContext> GetOrStartAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(CurrentSession ?? throw new InvalidOperationException());

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
            CancellationToken cancellationToken = default)
        {
            LastExpectedSessionKey = expectedSessionKey;
            if (CurrentSession is null
                || expectedSessionKey is not null
                && !string.Equals(
                    CurrentSession.SessionKey,
                    expectedSessionKey,
                    StringComparison.Ordinal))
            {
                return Task.FromResult(false);
            }

            CurrentSession = null;
            CurrentSessionChanged?.Invoke(this, EventArgs.Empty);
            return Task.FromResult(true);
        }
    }

    private sealed class IdleStimulationStateMachine : IStimulationStateMachine
    {
        public StimulationExecutionState CurrentState => StimulationExecutionState.Idle;

        public event EventHandler<StateTransition<StimulationExecutionState>>? StateChanged
        {
            add { }
            remove { }
        }

        public void MoveTo(
            StimulationExecutionState nextState,
            string trigger,
            string operatorId = "system")
        {
        }
    }

    private sealed class IdleEegRecordingService : IEegRecordingService
    {
        public bool IsRecording => false;

        public Task StartAsync(
            string recordName,
            EegAcquisitionConfig config,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public bool TryAppendSamples(EegSampleBatch batch) => false;

        public Task AppendSamplesAsync(
            EegSampleBatch batch,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task AddMarkerAsync(
            EegMarkerRecord marker,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task StopAsync(
            string status = "completed",
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class IdleCaptureMediaService : ICaptureMediaService
    {
        public event EventHandler<CaptureMediaCompleted>? Completed
        {
            add { }
            remove { }
        }

        public bool IsCapturing => false;

        public CaptureMediaSession? CurrentSession => null;

        public Task<CaptureMediaSession> StartAsync(
            CaptureMediaStartRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public void RequestStop(
            CaptureMediaStopReason reason,
            string? message = null)
        {
        }

        public Task StopAsync(
            CaptureMediaStopReason reason,
            string? message = null,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task WaitForIdleAsync(
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
