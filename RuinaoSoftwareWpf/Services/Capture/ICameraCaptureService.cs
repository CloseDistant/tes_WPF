namespace RuinaoSoftwareWpf;

public interface ICameraCaptureService : IAsyncDisposable
{
    bool IsOpen { get; }

    int RecordedFrameCount { get; }

    CameraCaptureProfileSnapshot? ActiveProfile { get; }

    string? LastOpenFailureMessage { get; }

    bool IsPreviewRenderingEnabled { get; }

    Task<bool> OpenAsync(
        int preferredIndex,
        string deviceKey,
        bool forceReopen = false,
        CancellationToken cancellationToken = default);

    bool TryTakeLatestPreview(out CameraPreviewSnapshot snapshot);

    bool TryTakeLatestFaceStatus(out CameraFaceStatusSnapshot snapshot);

    void SetPreviewRenderingEnabled(bool enabled);

    void SetRecordingEnabled(bool enabled);

    Task CloseAsync(CancellationToken cancellationToken = default);
}
