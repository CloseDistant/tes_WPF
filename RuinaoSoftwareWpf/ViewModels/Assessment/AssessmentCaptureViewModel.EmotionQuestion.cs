namespace RuinaoSoftwareWpf;

using System.Windows.Input;
using System.Windows.Threading;

/// <summary>
/// 情绪问答模块流程。
/// 第一题由用户手动开始；每题回答满 30 秒后允许手动完成，最长 120 秒自动结束；
/// 前两题结束后固定休息 12 秒。整个模块共用一段音视频，并通过毫秒级事件时间戳定位。
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
    private DateTimeOffset? currentEmotionQuestionRestStartedAt;
    private long? currentEmotionQuestionRestStartedTimestamp;
    private string emotionQuestionStatusText = string.Empty;
    private string emotionQuestionRestText = string.Empty;

    public ICommand StartEmotionQuestionCommand { get; private set; } = null!;

    public ICommand CompleteEmotionQuestionAnswerCommand => completeEmotionQuestionAnswerCommand;

    public bool IsEmotionQuestionWaiting => IsEmotionQuestionStage
        && emotionQuestionPhase == EmotionQuestionPhase.WaitingToStart;

    public bool IsEmotionQuestionAnswering => IsEmotionQuestionStage
        && IsEmotionQuestionAnsweringPhase;

    public bool IsEmotionQuestionResting => IsEmotionQuestionStage
        && emotionQuestionPhase == EmotionQuestionPhase.Resting;

    public bool IsEmotionQuestionPromptVisible => IsEmotionQuestionAnswering;

    public bool CanCompleteEmotionQuestionAnswer => IsEmotionQuestionStage
        && emotionQuestionPhase == EmotionQuestionPhase.AnsweringSubmittable;

    public string EmotionQuestionTitleText => T("CaptureWorkspaceEmotionQuestion");

    public string EmotionQuestionStartButtonText => T("CaptureWorkspaceEmotionQuestionStart");

    public string EmotionQuestionSubmitButtonText => T("CaptureWorkspaceEmotionQuestionSubmit");

    public string EmotionQuestionSubmitHintText => CanCompleteEmotionQuestionAnswer
        ? T(
            emotionQuestionIndex >= EmotionQuestionPrompts.Length - 1
                ? "CaptureWorkspaceEmotionQuestionSubmitFinalHint"
                : "CaptureWorkspaceEmotionQuestionSubmitHint",
            CaptureWorkbenchForcedRestSeconds)
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

    public string EmotionQuestionRestText
    {
        get => emotionQuestionRestText;
        private set => SetProperty(ref emotionQuestionRestText, value);
    }

    private bool IsEmotionQuestionAnsweringPhase => emotionQuestionPhase
        is EmotionQuestionPhase.AnsweringMinimum
        or EmotionQuestionPhase.AnsweringSubmittable;

    private void InitializeEmotionQuestionModule()
    {
        StartEmotionQuestionCommand = new RelayCommand(_ => StartFirstEmotionQuestion());
        completeEmotionQuestionAnswerCommand = new RelayCommand(
            _ => CompleteEmotionQuestionAnswerManually(),
            _ => CanCompleteEmotionQuestionAnswer);
        EmotionQuestionStatusText = T("CaptureWorkspaceRecordingPending");
        emotionQuestionTimer.Interval = TimeSpan.FromMilliseconds(250);
        emotionQuestionTimer.Tick += (_, _) => AdvanceEmotionQuestion();
    }

    /// <summary>
    /// 第一题由用户主动开始，后续题目由固定休息结束后自动推进。
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
        emotionQuestionRemainingSeconds = EmotionQuestionTimingRules.MinimumAnswerSeconds;
        currentEmotionQuestionStartedAt = null;
        currentEmotionQuestionStartedTimestamp = null;
        currentEmotionQuestionRestStartedAt = null;
        currentEmotionQuestionRestStartedTimestamp = null;
        emotionQuestionPhase = EmotionQuestionPhase.WaitingToStart;
        EmotionQuestionStatusText = T(
            "CaptureWorkspaceEmotionQuestionReady",
            1,
            EmotionQuestionPrompts.Length);
        EmotionQuestionRestText = string.Empty;
        StageNoticeText = string.Empty;
        NotifyEmotionQuestionStateChanged();
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
        currentEmotionQuestionStartedAt = timeProvider.GetUtcNow();
        currentEmotionQuestionStartedTimestamp = timeProvider.GetTimestamp();
        currentEmotionQuestionRestStartedAt = null;
        currentEmotionQuestionRestStartedTimestamp = null;
        emotionQuestionRemainingSeconds = EmotionQuestionTimingRules.MinimumAnswerSeconds;
        emotionQuestionPhase = EmotionQuestionPhase.AnsweringMinimum;
        StageNoticeText = string.Empty;
        EmotionQuestionRestText = string.Empty;
        UpdateEmotionQuestionStatusText(TimeSpan.Zero);

        RecordModuleEventSafely(
            "emotion_question_answer_started",
            $"情绪问答第 {emotionQuestionIndex + 1} 题开始",
            new
            {
                questionIndex = emotionQuestionIndex + 1,
                questionTotal = EmotionQuestionPrompts.Length,
                questionText = prompt.Text,
                questionType = prompt.QuestionType,
                minimumDurationSeconds = EmotionQuestionTimingRules.MinimumAnswerSeconds,
                maximumDurationSeconds = EmotionQuestionTimingRules.MaximumAnswerSeconds,
                startedAtUnixMs = currentEmotionQuestionStartedAt.Value.ToUnixTimeMilliseconds()
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

        if (emotionQuestionPhase != EmotionQuestionPhase.Resting
            || !currentEmotionQuestionRestStartedTimestamp.HasValue)
        {
            return;
        }

        var nowTimestamp = timeProvider.GetTimestamp();
        var elapsed = timeProvider.GetElapsedTime(
            currentEmotionQuestionRestStartedTimestamp.Value,
            nowTimestamp);
        if (elapsed >= TimeSpan.FromSeconds(CaptureWorkbenchForcedRestSeconds))
        {
            CompleteEmotionQuestionRest(nowTimestamp, timeProvider.GetUtcNow());
            return;
        }

        var remainingSeconds = EmotionQuestionTimingRules.RemainingSeconds(
            elapsed,
            CaptureWorkbenchForcedRestSeconds);
        if (remainingSeconds != emotionQuestionRemainingSeconds)
        {
            emotionQuestionRemainingSeconds = remainingSeconds;
            UpdateEmotionQuestionRestText();
        }
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
            "emotion_question_early_submit_enabled",
            $"情绪问答第 {emotionQuestionIndex + 1} 题允许手动完成",
            new
            {
                questionIndex = emotionQuestionIndex + 1,
                questionTotal = EmotionQuestionPrompts.Length,
                questionText = prompt.Text,
                questionType = prompt.QuestionType,
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
            ? "manual_submit"
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
                    questionText = prompt.Text,
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

        emotionQuestionPhase = EmotionQuestionPhase.Resting;
        emotionQuestionRemainingSeconds = CaptureWorkbenchForcedRestSeconds;
        currentEmotionQuestionRestStartedAt = completedAt;
        currentEmotionQuestionRestStartedTimestamp = completedTimestamp;
        EmotionQuestionStatusText = T(
            "CaptureWorkspaceEmotionQuestionCompletedCount",
            emotionQuestionIndex,
            EmotionQuestionPrompts.Length);
        UpdateEmotionQuestionRestText();

        RecordModuleEventSafely(
            "emotion_question_rest_started",
            $"情绪问答第 {completedQuestionIndex + 1} 题后休息开始",
            new
            {
                completedQuestionIndex = completedQuestionIndex + 1,
                nextQuestionIndex = emotionQuestionIndex + 1,
                durationSeconds = CaptureWorkbenchForcedRestSeconds,
                startedAtUnixMs = completedAt.ToUnixTimeMilliseconds()
            },
            completedAt,
            null);

        NotifyStageChanged();
        NotifyEmotionQuestionStateChanged();
    }

    private void CompleteEmotionQuestionRest(long completedTimestamp, DateTimeOffset completedAt)
    {
        if (emotionQuestionPhase != EmotionQuestionPhase.Resting)
        {
            return;
        }

        var durationMs = currentEmotionQuestionRestStartedTimestamp.HasValue
            ? (long)timeProvider.GetElapsedTime(
                currentEmotionQuestionRestStartedTimestamp.Value,
                completedTimestamp).TotalMilliseconds
            : 0L;
        RecordModuleEventSafely(
            "emotion_question_rest_completed",
            $"情绪问答第 {emotionQuestionIndex} 题后休息完成",
            new
            {
                completedQuestionIndex = emotionQuestionIndex,
                nextQuestionIndex = emotionQuestionIndex + 1,
                startedAtUnixMs = currentEmotionQuestionRestStartedAt?.ToUnixTimeMilliseconds(),
                endedAtUnixMs = completedAt.ToUnixTimeMilliseconds(),
                durationMs
            },
            currentEmotionQuestionRestStartedAt,
            completedAt);

        currentEmotionQuestionRestStartedAt = null;
        currentEmotionQuestionRestStartedTimestamp = null;
        StartEmotionQuestionAnswer();
    }

    private void CompleteEmotionQuestion()
    {
        emotionQuestionTimer.Stop();
        emotionQuestionPhase = EmotionQuestionPhase.Completed;
        emotionQuestionRemainingSeconds = 0;
        currentEmotionQuestionStartedAt = null;
        currentEmotionQuestionStartedTimestamp = null;
        currentEmotionQuestionRestStartedAt = null;
        currentEmotionQuestionRestStartedTimestamp = null;
        EmotionQuestionStatusText = T("CaptureWorkspaceEmotionQuestionCompleted");
        EmotionQuestionRestText = string.Empty;
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

    private void UpdateEmotionQuestionRestText()
    {
        EmotionQuestionRestText = T(
            "CaptureWorkspaceRestRemaining",
            emotionQuestionRemainingSeconds);
    }

    private void NotifyEmotionQuestionStateChanged()
    {
        OnPropertyChanged(nameof(IsEmotionQuestionWaiting));
        OnPropertyChanged(nameof(IsEmotionQuestionAnswering));
        OnPropertyChanged(nameof(IsEmotionQuestionResting));
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
        emotionQuestionRemainingSeconds = EmotionQuestionTimingRules.MinimumAnswerSeconds;
        currentEmotionQuestionStartedAt = null;
        currentEmotionQuestionStartedTimestamp = null;
        currentEmotionQuestionRestStartedAt = null;
        currentEmotionQuestionRestStartedTimestamp = null;
        EmotionQuestionStatusText = T("CaptureWorkspaceRecordingPending");
        EmotionQuestionRestText = string.Empty;
        NotifyEmotionQuestionStateChanged();
    }

    private enum EmotionQuestionCompletionReason
    {
        ManualSubmit,
        MaximumTimeout
    }
}
