namespace RuinaoSoftwareWpf;

using System.Windows.Input;
using System.Windows.Threading;

/// <summary>
/// 情绪问答模块流程。
/// 每题先呈现问题，最多思考 60 秒，可提前手动开始；每题回答满 20 秒后允许手动完成，最长 120 秒自动结束；
/// 题间不设置休息。整个模块共用一段音视频，并通过毫秒级事件时间戳定位。
/// </summary>
public sealed partial class AssessmentCaptureViewModel
{
    private readonly DispatcherTimer emotionQuestionTimer = new();
    private RelayCommand completeEmotionQuestionAnswerCommand = null!;
    private EmotionQuestionPhase emotionQuestionPhase = EmotionQuestionPhase.Idle;
    private int emotionQuestionIndex;
    private int emotionQuestionRemainingSeconds;
    private DateTimeOffset? currentEmotionQuestionStartedAt;
    private long? currentEmotionQuestionStartedTimestamp;
    private DateTimeOffset? currentEmotionQuestionPresentedAt;
    private long? currentEmotionQuestionPresentedTimestamp;
    private string emotionQuestionStatusText = string.Empty;

    public ICommand StartEmotionQuestionCommand { get; private set; } = null!;

    public ICommand CompleteEmotionQuestionAnswerCommand => completeEmotionQuestionAnswerCommand;

    public bool IsEmotionQuestionWaiting => IsEmotionQuestionStage
        && emotionQuestionPhase == EmotionQuestionPhase.WaitingToStart;

    public bool IsEmotionQuestionAnswering => IsEmotionQuestionStage
        && IsEmotionQuestionAnsweringPhase;

    public bool IsEmotionQuestionPromptVisible => IsEmotionQuestionWaiting || IsEmotionQuestionAnswering;

    public bool CanCompleteEmotionQuestionAnswer => IsEmotionQuestionStage
        && emotionQuestionPhase == EmotionQuestionPhase.AnsweringSubmittable;

    public string EmotionQuestionTitleText => T("CaptureWorkspaceEmotionQuestion");

    public string EmotionQuestionStartButtonText => T("CaptureWorkspaceEmotionQuestionStart");

    public string EmotionQuestionSubmitButtonText => T("CaptureWorkspaceEmotionQuestionSubmit");

    public string EmotionQuestionSubmitHintText => CanCompleteEmotionQuestionAnswer
        ? T(
            emotionQuestionIndex >= EmotionQuestionPrompts.Length - 1
                ? "CaptureWorkspaceEmotionQuestionSubmitFinalHint"
                : "CaptureWorkspaceEmotionQuestionSubmitHint")
        : T(
            "CaptureWorkspaceEmotionQuestionSubmitLockedHint",
            EmotionQuestionTimingRules.MinimumAnswerSeconds);

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

    private bool IsEmotionQuestionAnsweringPhase => emotionQuestionPhase
        is EmotionQuestionPhase.AnsweringMinimum
        or EmotionQuestionPhase.AnsweringSubmittable;

    private void InitializeEmotionQuestionModule()
    {
        StartEmotionQuestionCommand = new RelayCommand(_ => StartEmotionQuestionAnswer(), _ => IsEmotionQuestionWaiting);
        completeEmotionQuestionAnswerCommand = new RelayCommand(
            _ => CompleteEmotionQuestionAnswerManually(),
            _ => CanCompleteEmotionQuestionAnswer);
        EmotionQuestionStatusText = T("CaptureWorkspaceRecordingPending");
        emotionQuestionTimer.Interval = TimeSpan.FromMilliseconds(250);
        emotionQuestionTimer.Tick += (_, _) => AdvanceEmotionQuestion();
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
        PresentEmotionQuestion();
    }

    private void PresentEmotionQuestion()
    {
        var prompt = EmotionQuestionPrompts[emotionQuestionIndex];
        currentEmotionQuestionPresentedAt = timeProvider.GetUtcNow();
        currentEmotionQuestionPresentedTimestamp = timeProvider.GetTimestamp();
        currentEmotionQuestionStartedAt = null;
        currentEmotionQuestionStartedTimestamp = null;
        emotionQuestionPhase = EmotionQuestionPhase.WaitingToStart;
        StageNoticeText = string.Empty;
        UpdateEmotionQuestionThinkingText(TimeSpan.Zero);
        RecordModuleEventSafely(
            "emotion_question_presented",
            $"情绪问答第 {emotionQuestionIndex + 1} 题呈现",
            new
            {
                questionIndex = emotionQuestionIndex + 1,
                questionTotal = EmotionQuestionPrompts.Length,
                questionId = prompt.Id,
                questionVersion = prompt.Version,
                questionText = prompt.Text,
                presentedAtUnixMs = currentEmotionQuestionPresentedAt.Value.ToUnixTimeMilliseconds(),
                maximumThinkingSeconds = EmotionQuestionTimingRules.MaximumThinkingSeconds,
                isTest = false
            },
            currentEmotionQuestionPresentedAt,
            null);
        emotionQuestionTimer.Start();
        NotifyEmotionQuestionStateChanged();
    }

    private void StartEmotionQuestionAnswer()
    {
        if (!IsEmotionQuestionWaiting
            || currentStep != CaptureWorkbenchStep.ModuleExecution
            || emotionQuestionIndex >= EmotionQuestionPrompts.Length)
        {
            return;
        }

        var prompt = EmotionQuestionPrompts[emotionQuestionIndex];
        currentEmotionQuestionStartedAt = timeProvider.GetUtcNow();
        currentEmotionQuestionStartedTimestamp = timeProvider.GetTimestamp();
        emotionQuestionRemainingSeconds = EmotionQuestionTimingRules.MinimumAnswerSeconds;
        emotionQuestionPhase = EmotionQuestionPhase.AnsweringMinimum;
        StageNoticeText = string.Empty;
        UpdateEmotionQuestionStatusText(TimeSpan.Zero);

        RecordModuleEventSafely(
            "emotion_question_answer_started",
            $"情绪问答第 {emotionQuestionIndex + 1} 题开始",
            new
            {
                questionIndex = emotionQuestionIndex + 1,
                questionTotal = EmotionQuestionPrompts.Length,
                questionId = prompt.Id,
                questionVersion = prompt.Version,
                questionText = prompt.Text,
                questionType = prompt.QuestionType,
                minimumDurationSeconds = EmotionQuestionTimingRules.MinimumAnswerSeconds,
                maximumDurationSeconds = EmotionQuestionTimingRules.MaximumAnswerSeconds,
                presentedAtUnixMs = currentEmotionQuestionPresentedAt?.ToUnixTimeMilliseconds(),
                startedAtUnixMs = currentEmotionQuestionStartedAt.Value.ToUnixTimeMilliseconds(),
                isTest = false
            },
            currentEmotionQuestionStartedAt,
            null);

        emotionQuestionTimer.Start();
        NotifyStageChanged();
        NotifyEmotionQuestionStateChanged();
    }

    private void AdvanceEmotionQuestion()
    {
        if (!IsEmotionQuestionModule || currentStep != CaptureWorkbenchStep.ModuleExecution)
        {
            ResetEmotionQuestionState();
            NotifyStageChanged();
            return;
        }

        if (IsEmotionQuestionAnsweringPhase)
        {
            EvaluateEmotionQuestionAnswerTiming();
            return;
        }

        if (IsEmotionQuestionWaiting && currentEmotionQuestionPresentedTimestamp.HasValue)
        {
            var elapsed = timeProvider.GetElapsedTime(currentEmotionQuestionPresentedTimestamp.Value);
            if (EmotionQuestionTimingRules.ShouldStartAnswer(elapsed))
            {
                StartEmotionQuestionAnswer();
            }
            else
            {
                UpdateEmotionQuestionThinkingText(elapsed);
            }
        }
    }

    private void UpdateEmotionQuestionThinkingText(TimeSpan elapsed)
    {
        EmotionQuestionStatusText = T(
            "CaptureWorkspaceEmotionQuestionThinking",
            EmotionQuestionTimingRules.RemainingSeconds(elapsed, EmotionQuestionTimingRules.MaximumThinkingSeconds));
    }

    private void EvaluateEmotionQuestionAnswerTiming()
    {
        if (!currentEmotionQuestionStartedTimestamp.HasValue)
        {
            return;
        }

        var nowTimestamp = timeProvider.GetTimestamp();
        var elapsed = timeProvider.GetElapsedTime(
            currentEmotionQuestionStartedTimestamp.Value,
            nowTimestamp);
        var timingState = EmotionQuestionTimingRules.EvaluateAnswer(elapsed);

        if (emotionQuestionPhase == EmotionQuestionPhase.AnsweringMinimum
            && timingState != EmotionQuestionAnswerTimingState.MinimumDuration)
        {
            EnableEmotionQuestionManualSubmit(elapsed, timeProvider.GetUtcNow());
        }

        if (timingState == EmotionQuestionAnswerTimingState.MaximumReached)
        {
            CompleteCurrentEmotionQuestionAnswer(
                EmotionQuestionCompletionReason.MaximumTimeout,
                nowTimestamp,
                timeProvider.GetUtcNow());
            return;
        }

        UpdateEmotionQuestionStatusText(elapsed);
    }

    private void EnableEmotionQuestionManualSubmit(TimeSpan elapsed, DateTimeOffset enabledAt)
    {
        if (emotionQuestionPhase != EmotionQuestionPhase.AnsweringMinimum)
        {
            return;
        }

        emotionQuestionPhase = EmotionQuestionPhase.AnsweringSubmittable;
        var prompt = EmotionQuestionPrompts[emotionQuestionIndex];
        RecordModuleEventSafely(
            "emotion_question_finish_enabled",
            $"情绪问答第 {emotionQuestionIndex + 1} 题允许手动完成",
            new
            {
                questionIndex = emotionQuestionIndex + 1,
                questionTotal = EmotionQuestionPrompts.Length,
                questionId = prompt.Id,
                questionVersion = prompt.Version,
                questionText = prompt.Text,
                questionType = prompt.QuestionType,
                isTest = false,
                enabledAtUnixMs = enabledAt.ToUnixTimeMilliseconds(),
                elapsedMs = (long)elapsed.TotalMilliseconds
            },
            enabledAt,
            null);
        NotifyEmotionQuestionStateChanged();
    }

    private void CompleteEmotionQuestionAnswerManually()
    {
        EvaluateEmotionQuestionAnswerTiming();
        if (!CanCompleteEmotionQuestionAnswer)
        {
            return;
        }

        CompleteCurrentEmotionQuestionAnswer(
            EmotionQuestionCompletionReason.ManualSubmit,
            timeProvider.GetTimestamp(),
            timeProvider.GetUtcNow());
    }

    private void CompleteCurrentEmotionQuestionAnswer(
        EmotionQuestionCompletionReason completionReason,
        long completedTimestamp,
        DateTimeOffset completedAt)
    {
        if (!IsEmotionQuestionAnsweringPhase)
        {
            return;
        }

        emotionQuestionPhase = EmotionQuestionPhase.Idle;
        NotifyEmotionQuestionStateChanged();

        var completedQuestionIndex = emotionQuestionIndex;
        var prompt = completedQuestionIndex >= 0 && completedQuestionIndex < EmotionQuestionPrompts.Length
            ? EmotionQuestionPrompts[completedQuestionIndex]
            : null;
        var durationMs = currentEmotionQuestionStartedTimestamp.HasValue
            ? (long)timeProvider.GetElapsedTime(
                currentEmotionQuestionStartedTimestamp.Value,
                completedTimestamp).TotalMilliseconds
            : 0L;
        var completionReasonCode = completionReason == EmotionQuestionCompletionReason.ManualSubmit
            ? "manual_completed"
            : "maximum_timeout";

        if (prompt is not null)
        {
            RecordModuleEventSafely(
                "emotion_question_answer_completed",
                $"情绪问答第 {completedQuestionIndex + 1} 题完成",
                new
                {
                    questionIndex = completedQuestionIndex + 1,
                    questionTotal = EmotionQuestionPrompts.Length,
                    questionId = prompt.Id,
                    questionVersion = prompt.Version,
                    questionText = prompt.Text,
                    presentedAtUnixMs = currentEmotionQuestionPresentedAt?.ToUnixTimeMilliseconds(),
                    isTest = false,
                    questionType = prompt.QuestionType,
                    startedAtUnixMs = currentEmotionQuestionStartedAt?.ToUnixTimeMilliseconds(),
                    endedAtUnixMs = completedAt.ToUnixTimeMilliseconds(),
                    durationMs,
                    completionReason = completionReasonCode
                },
                currentEmotionQuestionStartedAt,
                completedAt);
        }

        emotionQuestionIndex++;
        currentEmotionQuestionStartedAt = null;
        currentEmotionQuestionStartedTimestamp = null;

        if (emotionQuestionIndex >= EmotionQuestionPrompts.Length)
        {
            CompleteEmotionQuestion();
            return;
        }

        PresentEmotionQuestion();
    }

    private void CompleteEmotionQuestion()
    {
        emotionQuestionTimer.Stop();
        emotionQuestionPhase = EmotionQuestionPhase.Completed;
        emotionQuestionRemainingSeconds = 0;
        currentEmotionQuestionStartedAt = null;
        currentEmotionQuestionStartedTimestamp = null;
        EmotionQuestionStatusText = T("CaptureWorkspaceEmotionQuestionCompleted");
        StageNoticeText = T("CaptureWorkspaceEmotionQuestionCompletedNotice");
        NotifyEmotionQuestionStateChanged();
        MoveToStep(CaptureWorkbenchStep.Completed);
        NotifyStageChanged();
    }

    private void UpdateEmotionQuestionStatusText(TimeSpan elapsed)
    {
        var elapsedSeconds = Math.Min(
            EmotionQuestionTimingRules.MaximumAnswerSeconds,
            Math.Max(0, (int)Math.Floor(elapsed.TotalSeconds)));
        if (emotionQuestionPhase == EmotionQuestionPhase.AnsweringMinimum)
        {
            emotionQuestionRemainingSeconds = EmotionQuestionTimingRules.RemainingSeconds(
                elapsed,
                EmotionQuestionTimingRules.MinimumAnswerSeconds);
            EmotionQuestionStatusText = T(
                "CaptureWorkspaceEmotionQuestionMinimumRemaining",
                elapsedSeconds,
                emotionQuestionRemainingSeconds);
            return;
        }

        emotionQuestionRemainingSeconds = EmotionQuestionTimingRules.RemainingSeconds(
            elapsed,
            EmotionQuestionTimingRules.MaximumAnswerSeconds);
        EmotionQuestionStatusText = T(
            "CaptureWorkspaceEmotionQuestionMaximumRemaining",
            elapsedSeconds,
            EmotionQuestionTimingRules.MaximumAnswerSeconds);
    }

    private void NotifyEmotionQuestionStateChanged()
    {
        OnPropertyChanged(nameof(IsEmotionQuestionWaiting));
        OnPropertyChanged(nameof(IsEmotionQuestionAnswering));
        OnPropertyChanged(nameof(EmotionQuestionText));
        OnPropertyChanged(nameof(EmotionQuestionProgressText));
        (StartEmotionQuestionCommand as RelayCommand)?.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(IsEmotionQuestionPromptVisible));
        OnPropertyChanged(nameof(CanCompleteEmotionQuestionAnswer));
        OnPropertyChanged(nameof(EmotionQuestionSubmitHintText));
        completeEmotionQuestionAnswerCommand?.RaiseCanExecuteChanged();
    }

    private void ResetEmotionQuestionState()
    {
        emotionQuestionTimer.Stop();
        emotionQuestionPhase = EmotionQuestionPhase.Idle;
        emotionQuestionIndex = 0;
        currentEmotionQuestionPresentedAt = null;
        currentEmotionQuestionPresentedTimestamp = null;
        emotionQuestionRemainingSeconds = EmotionQuestionTimingRules.MinimumAnswerSeconds;
        currentEmotionQuestionStartedAt = null;
        currentEmotionQuestionStartedTimestamp = null;
        EmotionQuestionStatusText = T("CaptureWorkspaceRecordingPending");
        NotifyEmotionQuestionStateChanged();
    }

    private enum EmotionQuestionCompletionReason
    {
        ManualSubmit,
        MaximumTimeout
    }
}
