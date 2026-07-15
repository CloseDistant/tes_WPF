namespace RuinaoHardwareDebugWpf;

using System.Windows.Input;
using System.Windows.Threading;

/// <summary>
/// 情绪问答模块流程。
/// 第一题由用户手动开始，之后按“回答 30 秒、休息 12 秒”自动推进。
/// 整个模块共用一段音视频，每题回答通过毫秒级事件时间戳定位。
/// </summary>
public sealed partial class AssessmentCaptureViewModel
{
    private readonly DispatcherTimer emotionQuestionTimer = new();
    private EmotionQuestionPhase emotionQuestionPhase = EmotionQuestionPhase.Idle;
    private int emotionQuestionIndex;
    private int emotionQuestionRemainingSeconds;
    private DateTimeOffset? currentEmotionQuestionStartedAt;
    private string emotionQuestionStatusText = string.Empty;
    private string emotionQuestionRestText = string.Empty;

    public ICommand StartEmotionQuestionCommand { get; private set; } = null!;

    public bool IsEmotionQuestionWaiting => IsEmotionQuestionStage
        && emotionQuestionPhase == EmotionQuestionPhase.WaitingToStart;

    public bool IsEmotionQuestionAnswering => IsEmotionQuestionStage
        && emotionQuestionPhase == EmotionQuestionPhase.Answering;

    public bool IsEmotionQuestionResting => IsEmotionQuestionStage
        && emotionQuestionPhase == EmotionQuestionPhase.Resting;

    public bool IsEmotionQuestionPromptVisible => IsEmotionQuestionAnswering;

    public string EmotionQuestionTitleText => T("CaptureWorkspaceEmotionQuestion");

    public string EmotionQuestionStartButtonText => T("CaptureWorkspaceEmotionQuestionStart");

    public string EmotionQuestionProgressText => T(
        "CaptureWorkspaceEmotionQuestionProgress",
        emotionQuestionIndex + 1,
        EmotionQuestionPrompts.Length);

    public string EmotionQuestionText => emotionQuestionIndex >= 0
        && emotionQuestionIndex < EmotionQuestionPrompts.Length
            ? EmotionQuestionPrompts[emotionQuestionIndex].Text
            : string.Empty;

    public string EmotionQuestionStatusText
    {
        get => emotionQuestionStatusText;
        private set => SetProperty(ref emotionQuestionStatusText, value);
    }

    public string EmotionQuestionRestText
    {
        get => emotionQuestionRestText;
        private set => SetProperty(ref emotionQuestionRestText, value);
    }

    private void InitializeEmotionQuestionModule()
    {
        StartEmotionQuestionCommand = new RelayCommand(_ => StartFirstEmotionQuestion());
        EmotionQuestionStatusText = T("CaptureWorkspaceRecordingPending");
        emotionQuestionTimer.Interval = TimeSpan.FromSeconds(1);
        emotionQuestionTimer.Tick += (_, _) => AdvanceEmotionQuestion();
    }

    /// <summary>
    /// 第一题由用户主动开始，后续题目由休息倒计时自动推进。
    /// </summary>
    private void StartFirstEmotionQuestion()
    {
        if (!IsEmotionQuestionWaiting || emotionQuestionIndex != 0)
        {
            return;
        }

        StartEmotionQuestionAnswer();
    }

    private void BeginEmotionQuestionSequence()
    {
        calibrationTimer.Stop();
        pictureBrowseTimer.Stop();
        videoBrowseTimer.Stop();
        voiceBaselineTimer.Stop();
        wordReadingTimer.Stop();
        shortTextReadingTimer.Stop();
        emotionQuestionTimer.Stop();

        emotionQuestionIndex = 0;
        emotionQuestionRemainingSeconds = EmotionQuestionAnswerSeconds;
        currentEmotionQuestionStartedAt = null;
        emotionQuestionPhase = EmotionQuestionPhase.WaitingToStart;
        EmotionQuestionStatusText = T(
            "CaptureWorkspaceEmotionQuestionReady",
            1,
            EmotionQuestionPrompts.Length);
        EmotionQuestionRestText = string.Empty;
        StageNoticeText = string.Empty;
    }

    private void StartEmotionQuestionAnswer()
    {
        if (!IsEmotionQuestionModule
            || currentStep != CaptureWorkbenchStep.ModuleExecution
            || emotionQuestionIndex >= EmotionQuestionPrompts.Length)
        {
            return;
        }

        var prompt = EmotionQuestionPrompts[emotionQuestionIndex];
        currentEmotionQuestionStartedAt = DateTimeOffset.Now;
        emotionQuestionRemainingSeconds = EmotionQuestionAnswerSeconds;
        emotionQuestionPhase = EmotionQuestionPhase.Answering;
        StageNoticeText = string.Empty;
        EmotionQuestionRestText = string.Empty;
        UpdateEmotionQuestionStatusText();

        RecordModuleEventSafely(
            "emotion_question_answer_started",
            $"情绪问答第 {emotionQuestionIndex + 1} 题开始",
            new
            {
                questionIndex = emotionQuestionIndex + 1,
                questionTotal = EmotionQuestionPrompts.Length,
                questionText = prompt.Text,
                questionType = prompt.QuestionType,
                fixedDurationSeconds = EmotionQuestionAnswerSeconds,
                startedAtUnixMs = currentEmotionQuestionStartedAt.Value.ToUnixTimeMilliseconds()
            },
            currentEmotionQuestionStartedAt,
            null);

        emotionQuestionTimer.Start();
        NotifyStageChanged();
    }

    private void AdvanceEmotionQuestion()
    {
        if (!IsEmotionQuestionModule || currentStep != CaptureWorkbenchStep.ModuleExecution)
        {
            ResetEmotionQuestionState();
            NotifyStageChanged();
            return;
        }

        if (emotionQuestionPhase == EmotionQuestionPhase.Answering)
        {
            if (emotionQuestionRemainingSeconds > 1)
            {
                emotionQuestionRemainingSeconds--;
                UpdateEmotionQuestionStatusText();
                NotifyStageChanged();
                return;
            }

            CompleteCurrentEmotionQuestionAnswer();
            return;
        }

        if (emotionQuestionPhase != EmotionQuestionPhase.Resting)
        {
            return;
        }

        if (emotionQuestionRemainingSeconds > 1)
        {
            emotionQuestionRemainingSeconds--;
            UpdateEmotionQuestionRestText();
            NotifyStageChanged();
            return;
        }

        StartEmotionQuestionAnswer();
    }

    private void CompleteCurrentEmotionQuestionAnswer()
    {
        var completedAt = DateTimeOffset.Now;
        var prompt = emotionQuestionIndex >= 0 && emotionQuestionIndex < EmotionQuestionPrompts.Length
            ? EmotionQuestionPrompts[emotionQuestionIndex]
            : null;
        var durationMs = currentEmotionQuestionStartedAt.HasValue
            ? (long)(completedAt - currentEmotionQuestionStartedAt.Value).TotalMilliseconds
            : 0L;

        if (prompt is not null)
        {
            RecordModuleEventSafely(
                "emotion_question_answer_completed",
                $"情绪问答第 {emotionQuestionIndex + 1} 题完成",
                new
                {
                    questionIndex = emotionQuestionIndex + 1,
                    questionTotal = EmotionQuestionPrompts.Length,
                    questionText = prompt.Text,
                    questionType = prompt.QuestionType,
                    startedAtUnixMs = currentEmotionQuestionStartedAt?.ToUnixTimeMilliseconds(),
                    endedAtUnixMs = completedAt.ToUnixTimeMilliseconds(),
                    durationMs
                },
                currentEmotionQuestionStartedAt,
                completedAt);
        }

        emotionQuestionIndex++;
        currentEmotionQuestionStartedAt = null;

        if (emotionQuestionIndex >= EmotionQuestionPrompts.Length)
        {
            CompleteEmotionQuestion();
            return;
        }

        emotionQuestionPhase = EmotionQuestionPhase.Resting;
        emotionQuestionRemainingSeconds = CaptureWorkbenchForcedRestSeconds;
        EmotionQuestionStatusText = T(
            "CaptureWorkspaceEmotionQuestionCompletedCount",
            emotionQuestionIndex,
            EmotionQuestionPrompts.Length);
        UpdateEmotionQuestionRestText();
        NotifyStageChanged();
    }

    private void CompleteEmotionQuestion()
    {
        emotionQuestionTimer.Stop();
        emotionQuestionPhase = EmotionQuestionPhase.Completed;
        emotionQuestionRemainingSeconds = 0;
        currentEmotionQuestionStartedAt = null;
        EmotionQuestionStatusText = T("CaptureWorkspaceEmotionQuestionCompleted");
        EmotionQuestionRestText = string.Empty;
        StageNoticeText = T("CaptureWorkspaceEmotionQuestionCompletedNotice");
        MoveToStep(CaptureWorkbenchStep.Completed);
        NotifyStageChanged();
    }

    private void UpdateEmotionQuestionStatusText()
    {
        EmotionQuestionStatusText = T(
            "CaptureWorkspaceEmotionQuestionRemaining",
            emotionQuestionRemainingSeconds);
    }

    private void UpdateEmotionQuestionRestText()
    {
        EmotionQuestionRestText = T(
            "CaptureWorkspaceRestRemaining",
            emotionQuestionRemainingSeconds);
    }

    private void ResetEmotionQuestionState()
    {
        emotionQuestionTimer.Stop();
        emotionQuestionPhase = EmotionQuestionPhase.Idle;
        emotionQuestionIndex = 0;
        emotionQuestionRemainingSeconds = EmotionQuestionAnswerSeconds;
        currentEmotionQuestionStartedAt = null;
        EmotionQuestionStatusText = T("CaptureWorkspaceRecordingPending");
        EmotionQuestionRestText = string.Empty;
    }
}
