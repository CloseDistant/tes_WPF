namespace RuinaoSoftwareWpf;

/// <summary>
/// 媒体应用服务与底层录制器之间的内部实现边界。
/// 该接口不向 ViewModel 或应用层契约暴露。
/// </summary>
internal interface ICaptureMediaBackend
{
    event EventHandler<CaptureRecordingCompletedEventArgs>? RecordingCompleted;

    bool IsRecording { get; }

    CaptureSessionInfo? CurrentSession { get; }

    Task<CaptureSessionInfo> StartAsync(
        CaptureRecordingRequest request,
        CancellationToken cancellationToken = default);

    Task RecordModuleEventAsync(
        CaptureSessionInfo session,
        string eventType,
        string? message = null,
        string? payloadJson = null,
        DateTimeOffset? startedAt = null,
        DateTimeOffset? endedAt = null,
        CancellationToken cancellationToken = default);

    void RequestStop(string status, string message);

    Task WaitForIdleAsync(CancellationToken cancellationToken = default);
}
