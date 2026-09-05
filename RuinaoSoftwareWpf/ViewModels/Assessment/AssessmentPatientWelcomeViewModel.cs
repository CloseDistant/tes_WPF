namespace RuinaoSoftwareWpf;

using System.Windows.Input;

/// <summary>
/// 患者端数字表型采集的简洁欢迎页。
/// </summary>
public sealed class AssessmentPatientWelcomeViewModel : ObservableObject
{
    private readonly ILocalizationService localization;
    private readonly RelayCommand enterCommand;

    public AssessmentPatientWelcomeViewModel(ILocalizationService localization)
    {
        this.localization = localization;
        enterCommand = new RelayCommand(_ => EnterRequested?.Invoke(this, EventArgs.Empty));
        EnterCommand = enterCommand;
        localization.LanguageChanged += (_, _) => NotifyTextChanged();
    }

    public event EventHandler? EnterRequested;

    public ICommand EnterCommand { get; }

    public string TitleText => localization.Text("AssessmentPatientWelcomeTitle");

    public string DescriptionText => localization.Text("AssessmentPatientWelcomeDescription");

    public string WelcomeMessageText => localization.Text("AssessmentPatientWelcomeMessage");

    public string EnterActionText => localization.Text("AssessmentPatientWelcomeEnter");

    private void NotifyTextChanged()
    {
        OnPropertyChanged(nameof(TitleText));
        OnPropertyChanged(nameof(DescriptionText));
        OnPropertyChanged(nameof(WelcomeMessageText));
        OnPropertyChanged(nameof(EnterActionText));
    }
}
