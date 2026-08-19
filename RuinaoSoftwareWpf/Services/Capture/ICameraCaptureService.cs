namespace RuinaoSoftwareWpf;

public interface ICameraCaptureService : IAsyncDisposable
{
    bool IsOpen { get; }

    int RecordedFrameCount { get; }

    Task<bool> OpenAsync(int preferredIndex, CancellationToken cancellationToken = default);

    bool TryTakeLatestPreview(out CameraPreviewSnapshot snapshot);

    void SetRecordingEnabled(bool enabled);

    Task CloseAsync(CancellationToken cancellationToken = default);
}
