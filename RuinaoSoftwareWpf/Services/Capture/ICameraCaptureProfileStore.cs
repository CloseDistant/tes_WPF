namespace RuinaoSoftwareWpf;

public interface ICameraCaptureProfileStore
{
    CameraOpeningPreference? Find(
        string deviceKey,
        CameraRecordingQualityMode recordingQualityMode);

    void Save(CameraOpeningPreference preference);
}
