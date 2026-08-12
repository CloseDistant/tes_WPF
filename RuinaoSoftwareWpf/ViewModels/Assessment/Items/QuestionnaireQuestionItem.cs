namespace RuinaoSoftwareWpf;

using System.Collections.ObjectModel;

public sealed class QuestionnaireQuestionItem : ObservableObject
{
    private string answerText = string.Empty;
    private string placeholderText;

    public QuestionnaireQuestionItem(
        int number,
        string text,
        string placeholderText,
        IReadOnlyList<string> answerOptions,
        int optionColumnCount = 1)
    {
        Number = number;
        Text = text;
        AnswerOptions = answerOptions;
        OptionColumnCount = Math.Clamp(optionColumnCount, 1, 2);
        OptionItems = new ObservableCollection<QuestionnaireAnswerOptionItem>(
            answerOptions.Select(static option => new QuestionnaireAnswerOptionItem(option)));
        this.placeholderText = placeholderText;
    }

    public int Number { get; }

    public string Text { get; }

    public IReadOnlyList<string> AnswerOptions { get; }

    public int OptionColumnCount { get; }

    /// <summary>
    /// 普通问卷选项区域宽度。单列按最长选项预留宽度，G/J 两列使用更宽区域保证一行两项舒展。
    /// </summary>
    public double OptionPanelWidth => OptionColumnCount == 1 ? 640 : 820;

    public ObservableCollection<QuestionnaireAnswerOptionItem> OptionItems { get; }

    /// <summary>
    /// 问卷题目根据字数自动调整字号，避免长题溢出、短题在大屏下过小。
    /// </summary>
    public double QuestionFontSize
    {
        get
        {
            var length = Text.Length;
            if (length <= 20)
            {
                return 26;
            }

            return length <= 35 ? 22 : 20;
        }
    }

    public double QuestionLineHeight => QuestionFontSize * 1.5;

    /// <summary>
    /// 是否为 0-10 评分题。评分题使用横向滑条展示，避免十一项纵向按钮超出显示区域。
    /// </summary>
    public bool IsZeroToTenQuestion => AnswerOptions.Count == 11
        && AnswerOptions.Select(static (option, index) => (option, index))
            .All(static item => string.Equals(item.option, item.index.ToString(), StringComparison.Ordinal));

    public string AnswerText
    {
        get => answerText;
        set
        {
            if (SetProperty(ref answerText, value))
            {
                RefreshOptionSelection();
                OnPropertyChanged(nameof(AnswerDisplayText));
                OnPropertyChanged(nameof(AnswerIndex));
                OnPropertyChanged(nameof(SelectionIndex));
                OnPropertyChanged(nameof(Score));
                OnPropertyChanged(nameof(ScoreValue));
            }
        }
    }

    public double ScoreValue
    {
        get => int.TryParse(AnswerText, out var numericScore) ? numericScore : 0;
        set
        {
            var roundedScore = Math.Clamp((int)Math.Round(value), 0, 10);
            AnswerText = roundedScore.ToString();
        }
    }

    public int AnswerIndex
    {
        get
        {
            for (var index = 0; index < AnswerOptions.Count; index++)
            {
                if (string.Equals(AnswerOptions[index], AnswerText, StringComparison.Ordinal))
                {
                    return index + 1;
                }
            }

            return 0;
        }
    }

    public int SelectionIndex
    {
        get => AnswerIndex - 1;
        set
        {
            if (value < 0 || value >= AnswerOptions.Count)
            {
                AnswerText = string.Empty;
                return;
            }

            AnswerText = AnswerOptions[value];
        }
    }

    public int Score => int.TryParse(AnswerText, out var numericScore)
        ? numericScore
        : AnswerIndex;

    public string AnswerDisplayText => string.IsNullOrWhiteSpace(AnswerText)
        ? placeholderText
        : AnswerText;

    public void UpdatePlaceholder(string value)
    {
        placeholderText = value;
        OnPropertyChanged(nameof(AnswerDisplayText));
    }

    /// <summary>
    /// 普通选择题使用按钮渲染，这里同步答案与按钮高亮状态，避免题目切换后残留上一题高亮。
    /// </summary>
    private void RefreshOptionSelection()
    {
        foreach (var option in OptionItems)
        {
            option.IsSelected = string.Equals(option.Text, AnswerText, StringComparison.Ordinal);
        }
    }
}
