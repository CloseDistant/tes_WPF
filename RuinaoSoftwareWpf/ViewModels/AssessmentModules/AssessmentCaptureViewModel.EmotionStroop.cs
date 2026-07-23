namespace RuinaoSoftwareWpf;

using System.Diagnostics;
using System.Windows.Input;
using System.Windows.Threading;

/// <summary>
/// 情绪 Stroop 模块流程。
/// 固定 60 试次共用一段连续音视频，试次事件以 Unix 毫秒时间戳与录制文件对齐。
/// </summary>
public sealed partial class AssessmentCaptureViewModel
{
    private const int EmotionStroopFixationMilliseconds = 1000;
    private const int EmotionStroopStimulusMilliseconds = 8000;
    private const int EmotionStroopPostBlankMilliseconds = 2200;
    private const int EmotionStroopRestSeconds = 15;

    private readonly DispatcherTimer emotionStroopTimer = new();
    private readonly Stopwatch emotionStroopResponseStopwatch = new();
    private EmotionStroopState emotionStroopState = EmotionStroopState.Idle;
    private int emotionStroopIndex;
    private int emotionStroopRestRemainingSeconds;
    private bool emotionStroopRestAfterBlank;
    private bool emotionStroopHasResponded;
    private bool emotionStroopIsCorrect;
    private long? emotionStroopResponseTimeMs;
    private DateTimeOffset? emotionStroopImageShownAt;
    private DateTimeOffset? emotionStroopAnsweredAt;

    public ICommand EmotionStroopRespondPositiveCommand { get; private set; } = null!;

    public ICommand EmotionStroopRespondNegativeCommand { get; private set; } = null!;

    public bool IsEmotionStroopFixation => IsEmotionStroopStage
        && emotionStroopState == EmotionStroopState.Fixation;

    public bool IsEmotionStroopStimulusVisible => IsEmotionStroopStage
        && emotionStroopState == EmotionStroopState.Stimulus;

    public bool IsEmotionStroopResting => IsEmotionStroopStage
        && emotionStroopState == EmotionStroopState.Resting;

    public bool ShowEmotionStroopResponseButtons => IsEmotionStroopStimulusVisible
        && !emotionStroopHasResponded;

    public string EmotionStroopImagePath => CurrentEmotionStroopTrial is null
        ? string.Empty
        : ResolveAssetPath(
            "Assets",
            "CaptureWorkbench",
            "EmotionStroop",
            CurrentEmotionStroopTrial.ImageFileName);

    public string EmotionStroopWordText => CurrentEmotionStroopTrial?.WordText ?? string.Empty;

    public string EmotionStroopPositiveText => T("CaptureWorkspaceEmotionStroopPositive");

    public string EmotionStroopNegativeText => T("CaptureWorkspaceEmotionStroopNegative");

    public string EmotionStroopRestTitleText => T("CaptureWorkspaceRestTitle");

    public string EmotionStroopRestText => T(
        "CaptureWorkspaceRestRemaining",
        emotionStroopRestRemainingSeconds);

    private EmotionStroopTrialDefinition? CurrentEmotionStroopTrial => emotionStroopIndex >= 0
        && emotionStroopIndex < EmotionStroopTrialCatalog.Trials.Count
            ? EmotionStroopTrialCatalog.Trials[emotionStroopIndex]
            : null;

    private void InitializeEmotionStroopModule()
    {
        EmotionStroopRespondPositiveCommand = new RelayCommand(
            _ => SubmitEmotionStroopResponse(EmotionStroopResponse.Positive));
        EmotionStroopRespondNegativeCommand = new RelayCommand(
            _ => SubmitEmotionStroopResponse(EmotionStroopResponse.Negative));
        emotionStroopTimer.Tick += (_, _) => AdvanceEmotionStroop();
    }

    private void BeginEmotionStroopSequence()
    {
        StopModuleExecutionTimers();
        emotionStroopIndex = 0;
        StageNoticeText = string.Empty;
        StartEmotionStroopTrial();
    }

    private void StartEmotionStroopTrial()
    {
        if (!IsEmotionStroopModule
            || currentStep != CaptureWorkbenchStep.ModuleExecution
            || CurrentEmotionStroopTrial is null)
        {
            return;
        }

        ResetCurrentEmotionStroopTrialResult();
        emotionStroopState = EmotionStroopState.Fixation;
        StartEmotionStroopTimer(EmotionStroopFixationMilliseconds);
        NotifyStageChanged();
    }

    private void AdvanceEmotionStroop()
    {
        emotionStroopTimer.Stop();
        if (!IsEmotionStroopModule || currentStep != CaptureWorkbenchStep.ModuleExecution)
        {
            ResetEmotionStroopState();
            NotifyStageChanged();
            return;
        }

        var now = DateTimeOffset.Now;
        switch (emotionStroopState)
        {
            case EmotionStroopState.Fixation:
                emotionStroopState = EmotionStroopState.Stimulus;
                emotionStroopImageShownAt = now;
                emotionStroopResponseStopwatch.Restart();
                StartEmotionStroopTimer(EmotionStroopStimulusMilliseconds);
                break;
            case EmotionStroopState.Stimulus:
                emotionStroopResponseStopwatch.Stop();
                CompleteCurrentEmotionStroopTrial(now);
                return;
            case EmotionStroopState.PostBlank:
                if (emotionStroopRestAfterBlank)
                {
                    emotionStroopRestAfterBlank = false;
                    emotionStroopState = EmotionStroopState.Resting;
                    emotionStroopRestRemainingSeconds = EmotionStroopRestSeconds;
                    emotionStroopTimer.Interval = TimeSpan.FromSeconds(1);
                    emotionStroopTimer.Start();
                    break;
                }

                StartEmotionStroopTrial();
                return;
            case EmotionStroopState.Resting:
                AdvanceEmotionStroopRest();
                return;
            default:
                return;
        }

        NotifyEmotionStroopStateChanged();
    }

    private void SubmitEmotionStroopResponse(EmotionStroopResponse response)
    {
        if (!IsEmotionStroopStimulusVisible
            || emotionStroopHasResponded
            || CurrentEmotionStroopTrial is null)
        {
            return;
        }

        emotionStroopResponseStopwatch.Stop();
        emotionStroopHasResponded = true;
        emotionStroopResponseTimeMs = emotionStroopResponseStopwatch.ElapsedMilliseconds;
        emotionStroopAnsweredAt = DateTimeOffset.Now;
        emotionStroopIsCorrect = response == CurrentEmotionStroopTrial.CorrectResponse;
        NotifyEmotionStroopStateChanged();
    }

    private void CompleteCurrentEmotionStroopTrial(DateTimeOffset completedAt)
    {
        var trial = CurrentEmotionStroopTrial;
        if (trial is null)
        {
            return;
        }

        RecordModuleEventSafely(
            "emotion_stroop_trial_completed",
            $"情绪 Stroop 第 {trial.TrialIndex} 试次完成",
            new
            {
                configurationVersion = EmotionStroopTrialCatalog.ConfigurationVersion,
                trialIndex = trial.TrialIndex,
                imageShownAtUnixMs = UnixMilliseconds(emotionStroopImageShownAt),
                answeredAtUnixMs = UnixMilliseconds(emotionStroopAnsweredAt),
                responseTimeMs = emotionStroopResponseTimeMs,
                isCorrect = emotionStroopHasResponded && emotionStroopIsCorrect,
                trialEndedAtUnixMs = completedAt.ToUnixTimeMilliseconds()
            },
            emotionStroopImageShownAt,
            completedAt);

        emotionStroopIndex++;
        if (emotionStroopIndex >= EmotionStroopTrialCatalog.Trials.Count)
        {
            CompleteEmotionStroopSequence();
            return;
        }

        emotionStroopState = EmotionStroopState.PostBlank;
        emotionStroopRestAfterBlank = trial.TrialIndex is 20 or 40;
        StartEmotionStroopTimer(EmotionStroopPostBlankMilliseconds);
        NotifyEmotionStroopStateChanged();
    }

    private void AdvanceEmotionStroopRest()
    {
        if (emotionStroopRestRemainingSeconds > 1)
        {
            emotionStroopRestRemainingSeconds--;
            emotionStroopTimer.Interval = TimeSpan.FromSeconds(1);
            emotionStroopTimer.Start();
            NotifyEmotionStroopStateChanged();
            return;
        }

        emotionStroopRestRemainingSeconds = 0;
        StartEmotionStroopTrial();
    }

    private void CompleteEmotionStroopSequence()
    {
        emotionStroopTimer.Stop();
        emotionStroopResponseStopwatch.Reset();
        emotionStroopState = EmotionStroopState.Completed;
        emotionStroopRestRemainingSeconds = 0;
        StageNoticeText = T("CaptureWorkspaceEmotionStroopCompletedNotice");
        MoveToStep(CaptureWorkbenchStep.Completed);
        NotifyStageChanged();
    }

    private void StartEmotionStroopTimer(int milliseconds)
    {
        emotionStroopTimer.Stop();
        emotionStroopTimer.Interval = TimeSpan.FromMilliseconds(milliseconds);
        emotionStroopTimer.Start();
    }

    private void ResetCurrentEmotionStroopTrialResult()
    {
        emotionStroopResponseStopwatch.Reset();
        emotionStroopHasResponded = false;
        emotionStroopIsCorrect = false;
        emotionStroopResponseTimeMs = null;
        emotionStroopImageShownAt = null;
        emotionStroopAnsweredAt = null;
    }

    private void ResetEmotionStroopState()
    {
        emotionStroopTimer.Stop();
        emotionStroopResponseStopwatch.Reset();
        emotionStroopState = EmotionStroopState.Idle;
        emotionStroopIndex = 0;
        emotionStroopRestRemainingSeconds = 0;
        emotionStroopRestAfterBlank = false;
        ResetCurrentEmotionStroopTrialResult();
    }

    private void NotifyEmotionStroopStateChanged()
    {
        OnPropertyChanged(nameof(IsEmotionStroopFixation));
        OnPropertyChanged(nameof(IsEmotionStroopStimulusVisible));
        OnPropertyChanged(nameof(IsEmotionStroopResting));
        OnPropertyChanged(nameof(ShowEmotionStroopResponseButtons));
        OnPropertyChanged(nameof(EmotionStroopImagePath));
        OnPropertyChanged(nameof(EmotionStroopWordText));
        OnPropertyChanged(nameof(EmotionStroopPositiveText));
        OnPropertyChanged(nameof(EmotionStroopNegativeText));
        OnPropertyChanged(nameof(EmotionStroopRestTitleText));
        OnPropertyChanged(nameof(EmotionStroopRestText));
    }
}
