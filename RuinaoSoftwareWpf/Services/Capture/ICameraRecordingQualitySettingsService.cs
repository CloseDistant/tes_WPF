namespace RuinaoSoftwareWpf;

public interface ICameraRecordingQualitySettingsService
{
    CameraRecordingQualityMode SelectedMode { get; }

    CameraCaptureProfile SelectedProfile { get; }

    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(
        CameraRecordingQualityMode mode,
        CancellationToken cancellationToken = default);
}
