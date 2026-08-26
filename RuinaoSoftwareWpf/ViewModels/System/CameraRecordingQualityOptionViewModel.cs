namespace RuinaoSoftwareWpf;

public sealed class CameraRecordingQualityOptionViewModel : ObservableObject
{
    private bool isSelected;

    public CameraRecordingQualityOptionViewModel(CameraRecordingQualityMode mode)
    {
        Mode = mode;
        DisplayName = CameraRecordingQualityCatalog.DisplayName(mode);
        Specification = CameraRecordingQualityCatalog.Specification(mode);
        Description = CameraRecordingQualityCatalog.Description(mode);
        PerformanceNote = CameraRecordingQualityCatalog.PerformanceNote(mode);
    }

    public CameraRecordingQualityMode Mode { get; }

    public string DisplayName { get; }

    public string Specification { get; }

    public string Description { get; }

    public string PerformanceNote { get; }

    public bool IsSelected
    {
        get => isSelected;
        set => SetProperty(ref isSelected, value);
    }
}
