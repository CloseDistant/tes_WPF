namespace RuinaoSoftwareWpf;

using System.Collections.ObjectModel;

public sealed class QuestionnaireSessionState
{
    private int currentIndex;

    public ObservableCollection<QuestionnaireQuestionItem> Questions { get; } = [];

    public QuestionnaireQuestionItem? Current => Questions.Count == 0
        ? null
        : Questions[Math.Clamp(currentIndex, 0, Questions.Count - 1)];

    public int CurrentNumber => Questions.Count == 0 ? 0 : currentIndex + 1;

    public bool CanMovePrevious => currentIndex > 0;

    public bool CanMoveNext => currentIndex + 1 < Questions.Count;

    public void Clear()
    {
        Questions.Clear();
        currentIndex = 0;
    }

    public bool MovePrevious()
    {
        if (!CanMovePrevious)
        {
            return false;
        }

        currentIndex--;
        return true;
    }

    public bool MoveNext()
    {
        if (!CanMoveNext)
        {
            return false;
        }

        currentIndex++;
        return true;
    }

    public void MoveTo(QuestionnaireQuestionItem question)
    {
        var index = Questions.IndexOf(question);
        if (index >= 0)
        {
            currentIndex = index;
        }
    }

    public void Reset(bool clearAnswers)
    {
        currentIndex = 0;
        if (!clearAnswers)
        {
            return;
        }

        foreach (var question in Questions)
        {
            question.AnswerText = string.Empty;
        }
    }
}
