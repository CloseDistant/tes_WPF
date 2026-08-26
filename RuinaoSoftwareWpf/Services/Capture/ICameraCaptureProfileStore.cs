namespace RuinaoSoftwareWpf;

public interface ICameraCaptureProfileStore
{
    CameraOpeningPreference? Find(string deviceKey);

    void Save(CameraOpeningPreference preference);
}
