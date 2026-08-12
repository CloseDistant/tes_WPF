namespace RuinaoSoftwareWpf;

using RuinaoSoftwareWpf.ApplicationContracts;

public abstract class AssessmentModuleViewModel : ObservableObject, IAssessmentModuleDescriptor
{
    protected AssessmentModuleViewModel(string code, string displayNameKey, bool isDevelopmentOnly)
    {
        Code = code;
        DisplayNameKey = displayNameKey;
        IsDevelopmentOnly = isDevelopmentOnly;
    }

    public string Code { get; }
    public string DisplayNameKey { get; }
    public bool IsDevelopmentOnly { get; }
    public abstract AssessmentModuleKind Kind { get; }
    public AssessmentModuleDefinition Definition => new(Code, DisplayNameKey, Kind, IsDevelopmentOnly);
}
