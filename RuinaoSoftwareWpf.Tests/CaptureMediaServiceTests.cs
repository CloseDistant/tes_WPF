namespace RuinaoSoftwareWpf.Tests;

using RuinaoSoftwareWpf.ApplicationContracts;
using Xunit;

public sealed class CaptureMediaServiceTests
{
    [Fact]
    public async Task StartAsync_MapsBackendSessionWithoutChangingOutputDirectory()
    {
        var backend = new FakeCaptureMediaBackend();
        var service = new CaptureMediaService(backend, TimeProvider.System);

        var session = await service.StartAsync(new CaptureMediaStartRequest(
            101,
            "session-001",
            "module-001",
            "测试模块",
            "camera-001"),
            TestContext.Current.CancellationToken);

        Assert.Equal("session-001", backend.LastStartRequest?.SessionKey);
        Assert.Equal("module-001", session.ModuleCode);
        Assert.Equal(backend.Session.OutputDirectory, session.OutputDirectory);
        Assert.True(service.IsCapturing);
    }

    [Theory]
    [InlineData(CaptureMediaStopReason.Completed, "completed")]
    [InlineData(CaptureMediaStopReason.Interrupted, "interrupted")]
    [InlineData(CaptureMediaStopReason.Discarded, "discarded")]
    [InlineData(CaptureMediaStopReason.Failed, "merge_failed")]
    public void RequestStop_MapsApplicationReasonToExistingRecorderStatus(
        CaptureMediaStopReason reason,
        string expectedStatus)
    {
        var backend = new FakeCaptureMediaBackend();
        var service = new CaptureMediaService(backend, TimeProvider.System);

        service.RequestStop(reason, "message");

        Assert.Equal(expectedStatus, backend.LastStopStatus);
        Assert.Equal("message", backend.LastStopMessage);
    }

    [Fact]
    public async Task BackendCompletion_MapsProbeFailureToCompletedWithWarnings()
    {
        var backend = new FakeCaptureMediaBackend();
        var service = new CaptureMediaService(backend, TimeProvider.System);
        CaptureMediaCompleted? completed = null;
        service.Completed += (_, result) => completed = result;
        await service.StartAsync(new CaptureMediaStartRequest(
            101,
            "session-001",
            "module-001",
            "测试模块",
            "camera-001"),
            TestContext.Current.CancellationToken);

        backend.RaiseCompleted("completed_with_probe_error", "同步校验失败");

        Assert.NotNull(completed);
        Assert.Equal(CaptureMediaCompletionStatus.CompletedWithWarnings, completed.Status);
        Assert.Equal("MEDIA_SYNC_PROBE_FAILED", completed.ErrorCode);
        Assert.Null(service.CurrentSession);
    }

    private sealed class FakeCaptureMediaBackend : ICaptureMediaBackend
    {
        public CaptureSessionInfo Session { get; } = new(
            1,
            2,
            3,
            101,
            "session-001",
            "module-001",
            "测试模块",
            "database.db",
            Path.Combine("capture", "session-001", "module-001"),
            "raw.avi",
            "normalized.avi",
            "audio.wav",
            "merged.mp4");

        public event EventHandler<CaptureRecordingCompletedEventArgs>? RecordingCompleted;

        public bool IsRecording { get; private set; }

        public CaptureSessionInfo? CurrentSession => IsRecording ? Session : null;

        public CaptureRecordingRequest? LastStartRequest { get; private set; }

        public string? LastStopStatus { get; private set; }

        public string? LastStopMessage { get; private set; }

        public Task<CaptureSessionInfo> StartAsync(
            CaptureRecordingRequest request,
            CancellationToken cancellationToken = default)
        {
            LastStartRequest = request;
            IsRecording = true;
            return Task.FromResult(Session);
        }

        public Task RecordModuleEventAsync(
            CaptureSessionInfo session,
            string eventType,
            string? message = null,
            string? payloadJson = null,
            DateTimeOffset? startedAt = null,
            DateTimeOffset? endedAt = null,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public void RequestStop(string status, string message)
        {
            LastStopStatus = status;
            LastStopMessage = message;
            IsRecording = false;
        }

        public Task WaitForIdleAsync(
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public void RaiseCompleted(string status, string message)
        {
            IsRecording = false;
            RecordingCompleted?.Invoke(
                this,
                new CaptureRecordingCompletedEventArgs(Session, status, message));
        }
    }
}
