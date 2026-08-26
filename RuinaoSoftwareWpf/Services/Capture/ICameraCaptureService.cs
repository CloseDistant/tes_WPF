namespace RuinaoSoftwareWpf;

public interface ICameraCaptureService : IAsyncDisposable
{
    bool IsOpen { get; }

    int RecordedFrameCount { get; }

    CameraCaptureProfileSnapshot? ActiveProfile { get; }

    string? LastOpenFailureMessage { get; }

    Task<bool> OpenAsync(
        int preferredIndex,
        string deviceKey,
        bool forceReopen = false,
        CancellationToken cancellationToken = default);

    bool TryTakeLatestPreview(out CameraPreviewSnapshot snapshot);

    void SetRecordingEnabled(bool enabled);

    Task CloseAsync(CancellationToken cancellationToken = default);
}
