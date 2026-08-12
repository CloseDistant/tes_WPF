namespace RuinaoSoftwareWpf;

public sealed class QuestionnaireAnswerOptionItem : ObservableObject
{
    private bool isSelected;

    public QuestionnaireAnswerOptionItem(string text)
    {
        Text = text;
    }

    public string Text { get; }

    public bool IsSelected
    {
        get => isSelected;
        set => SetProperty(ref isSelected, value);
    }
}
