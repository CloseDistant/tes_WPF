namespace RuinaoSoftwareWpf;

using System.Diagnostics;
using System.Windows.Input;
using System.Windows.Threading;

/// <summary>
/// 情绪字母搜索模块流程。
/// 固定 48 试次共用一段连续音视频，试次事件以 Unix 毫秒时间戳与录制文件对齐。
/// </summary>
public sealed partial class AssessmentCaptureViewModel
{
    private const int EmotionLetterSearchFixationMilliseconds = 1000;
    private const int EmotionLetterSearchImageMilliseconds = 8000;
    private const int EmotionLetterSearchPostBlankMilliseconds = 2000;
    private const int EmotionLetterSearchRestSeconds = 15;

    private static readonly int[] EmotionLetterSearchLetterDelays = [1000, 1500, 2000];
    private readonly DispatcherTimer emotionLetterSearchTimer = new();
    private readonly Stopwatch emotionLetterSearchResponseStopwatch = new();
    private EmotionLetterSearchState emotionLetterSearchState = EmotionLetterSearchState.Idle;
    private int emotionLetterSearchIndex;
    private int emotionLetterSearchLetterDelayMs;
    private int emotionLetterSearchRestRemainingSeconds;
    private bool emotionLetterSearchRestAfterBlank;
    private bool emotionLetterSearchHasResponded;
    private bool emotionLetterSearchIsCorrect;
    private long? emotionLetterSearchResponseTimeMs;
    private DateTimeOffset? emotionLetterSearchImageShownAt;
    private DateTimeOffset? emotionLetterSearchLettersShownAt;
    private DateTimeOffset? emotionLetterSearchAnsweredAt;

    public ICommand EmotionLetterSearchRespondOneCommand { get; private set; } = null!;

    public ICommand EmotionLetterSearchRespondTwoCommand { get; private set; } = null!;

    public bool IsEmotionLetterSearchFixation => IsEmotionLetterSearchStage
        && emotionLetterSearchState == EmotionLetterSearchState.Fixation;

    public bool IsEmotionLetterSearchImageVisible => IsEmotionLetterSearchStage
        && emotionLetterSearchState is EmotionLetterSearchState.ImageOnly or EmotionLetterSearchState.ImageWithLetters;

    public bool IsEmotionLetterSearchLettersVisible => IsEmotionLetterSearchStage
        && emotionLetterSearchState == EmotionLetterSearchState.ImageWithLetters;

    public bool IsEmotionLetterSearchResting => IsEmotionLetterSearchStage
        && emotionLetterSearchState == EmotionLetterSearchState.Resting;

    public bool ShowEmotionLetterSearchResponseButtons => IsEmotionLetterSearchLettersVisible
        && !emotionLetterSearchHasResponded;

    public string EmotionLetterSearchImagePath => CurrentEmotionLetterSearchTrial is null
        ? string.Empty
        : ResolveAssetPath(
            "Assets",
            "CaptureWorkbench",
            "EmotionLetterSearch",
            CurrentEmotionLetterSearchTrial.ImageFileName);

    public string EmotionLetterSearchLetters => CurrentEmotionLetterSearchTrial?.Letters ?? string.Empty;

    public double EmotionLetterSearchLetterLeft => CurrentEmotionLetterSearchTrial?.LetterPosition switch
    {
        1 or 4 => 12,
        2 or 5 => 214,
        3 or 6 => 416,
        _ => 214
    };

    public double EmotionLetterSearchLetterTop => CurrentEmotionLetterSearchTrial?.LetterPosition switch
    {
        1 or 2 or 3 => 190,
        4 or 5 or 6 => 455,
        _ => 322
    };

    public string EmotionLetterSearchRestTitleText => T("CaptureWorkspaceRestTitle");

    public string EmotionLetterSearchRestText => T(
        "CaptureWorkspaceRestRemaining",
        emotionLetterSearchRestRemainingSeconds);

    public string EmotionLetterSearchButtonOneText => "1";

    public string EmotionLetterSearchButtonTwoText => "2";

    private EmotionLetterSearchTrialDefinition? CurrentEmotionLetterSearchTrial => emotionLetterSearchIndex >= 0
        && emotionLetterSearchIndex < EmotionLetterSearchTrialCatalog.Trials.Count
            ? EmotionLetterSearchTrialCatalog.Trials[emotionLetterSearchIndex]
            : null;

    private void InitializeEmotionLetterSearchModule()
    {
        EmotionLetterSearchRespondOneCommand = new RelayCommand(
            _ => SubmitEmotionLetterSearchResponse(EmotionLetterSearchResponse.ContainsX));
        EmotionLetterSearchRespondTwoCommand = new RelayCommand(
            _ => SubmitEmotionLetterSearchResponse(EmotionLetterSearchResponse.ContainsN));
        emotionLetterSearchTimer.Tick += (_, _) => AdvanceEmotionLetterSearch();
    }

    private void BeginEmotionLetterSearchSequence()
    {
        StopModuleExecutionTimers();
        emotionLetterSearchIndex = 0;
        StageNoticeText = string.Empty;
        StartEmotionLetterSearchTrial();
    }

    private void StartEmotionLetterSearchTrial()
    {
        if (!IsEmotionLetterSearchModule
            || currentStep != CaptureWorkbenchStep.ModuleExecution
            || CurrentEmotionLetterSearchTrial is null)
        {
            return;
        }

        ResetCurrentEmotionLetterSearchTrialResult();
        emotionLetterSearchLetterDelayMs = EmotionLetterSearchLetterDelays[
            Random.Shared.Next(EmotionLetterSearchLetterDelays.Length)];
        emotionLetterSearchState = EmotionLetterSearchState.Fixation;
        StartEmotionLetterSearchTimer(EmotionLetterSearchFixationMilliseconds);
        NotifyStageChanged();
    }

    private void AdvanceEmotionLetterSearch()
    {
        emotionLetterSearchTimer.Stop();
        if (!IsEmotionLetterSearchModule || currentStep != CaptureWorkbenchStep.ModuleExecution)
        {
            ResetEmotionLetterSearchState();
            NotifyStageChanged();
            return;
        }

        var now = DateTimeOffset.Now;
        switch (emotionLetterSearchState)
        {
            case EmotionLetterSearchState.Fixation:
                emotionLetterSearchState = EmotionLetterSearchState.ImageOnly;
                emotionLetterSearchImageShownAt = now;
                StartEmotionLetterSearchTimer(emotionLetterSearchLetterDelayMs);
                break;
            case EmotionLetterSearchState.ImageOnly:
                emotionLetterSearchState = EmotionLetterSearchState.ImageWithLetters;
                emotionLetterSearchLettersShownAt = now;
                emotionLetterSearchResponseStopwatch.Restart();
                StartEmotionLetterSearchTimer(EmotionLetterSearchImageMilliseconds - emotionLetterSearchLetterDelayMs);
                break;
            case EmotionLetterSearchState.ImageWithLetters:
                emotionLetterSearchResponseStopwatch.Stop();
                CompleteCurrentEmotionLetterSearchTrial(now);
                return;
            case EmotionLetterSearchState.PostBlank:
                if (emotionLetterSearchRestAfterBlank)
                {
                    emotionLetterSearchRestAfterBlank = false;
                    emotionLetterSearchState = EmotionLetterSearchState.Resting;
                    emotionLetterSearchRestRemainingSeconds = EmotionLetterSearchRestSeconds;
                    emotionLetterSearchTimer.Interval = TimeSpan.FromSeconds(1);
                    emotionLetterSearchTimer.Start();
                    break;
                }

                StartEmotionLetterSearchTrial();
                return;
            case EmotionLetterSearchState.Resting:
                AdvanceEmotionLetterSearchRest();
                return;
            default:
                return;
        }

        NotifyEmotionLetterSearchStateChanged();
    }

    private void SubmitEmotionLetterSearchResponse(EmotionLetterSearchResponse response)
    {
        if (!IsEmotionLetterSearchLettersVisible
            || emotionLetterSearchHasResponded
            || CurrentEmotionLetterSearchTrial is null)
        {
            return;
        }

        emotionLetterSearchResponseStopwatch.Stop();
        emotionLetterSearchHasResponded = true;
        emotionLetterSearchResponseTimeMs = emotionLetterSearchResponseStopwatch.ElapsedMilliseconds;
        emotionLetterSearchAnsweredAt = DateTimeOffset.Now;
        emotionLetterSearchIsCorrect = response == CurrentEmotionLetterSearchTrial.CorrectResponse;
        NotifyEmotionLetterSearchStateChanged();
    }

    private void CompleteCurrentEmotionLetterSearchTrial(DateTimeOffset completedAt)
    {
        var trial = CurrentEmotionLetterSearchTrial;
        if (trial is null)
        {
            return;
        }

        RecordModuleEventSafely(
            "emotion_letter_search_trial_completed",
            $"情绪字母搜索第 {trial.TrialIndex} 试次完成",
            new
            {
                configurationVersion = EmotionLetterSearchTrialCatalog.ConfigurationVersion,
                trialIndex = trial.TrialIndex,
                letterDelayMs = emotionLetterSearchLetterDelayMs,
                imageShownAtUnixMs = UnixMilliseconds(emotionLetterSearchImageShownAt),
                lettersShownAtUnixMs = UnixMilliseconds(emotionLetterSearchLettersShownAt),
                answeredAtUnixMs = UnixMilliseconds(emotionLetterSearchAnsweredAt),
                responseTimeMs = emotionLetterSearchResponseTimeMs,
                isCorrect = emotionLetterSearchHasResponded && emotionLetterSearchIsCorrect,
                trialEndedAtUnixMs = completedAt.ToUnixTimeMilliseconds()
            },
            emotionLetterSearchImageShownAt,
            completedAt);

        emotionLetterSearchIndex++;
        if (emotionLetterSearchIndex >= EmotionLetterSearchTrialCatalog.Trials.Count)
        {
            CompleteEmotionLetterSearchSequence();
            return;
        }

        emotionLetterSearchState = EmotionLetterSearchState.PostBlank;
        emotionLetterSearchRestAfterBlank = trial.TrialIndex is 16 or 32;
        StartEmotionLetterSearchTimer(EmotionLetterSearchPostBlankMilliseconds);
        NotifyEmotionLetterSearchStateChanged();
    }

    private void AdvanceEmotionLetterSearchRest()
    {
        if (emotionLetterSearchRestRemainingSeconds > 1)
        {
            emotionLetterSearchRestRemainingSeconds--;
            emotionLetterSearchTimer.Interval = TimeSpan.FromSeconds(1);
            emotionLetterSearchTimer.Start();
            NotifyEmotionLetterSearchStateChanged();
            return;
        }

        emotionLetterSearchRestRemainingSeconds = 0;
        StartEmotionLetterSearchTrial();
    }

    private void CompleteEmotionLetterSearchSequence()
    {
        emotionLetterSearchTimer.Stop();
        emotionLetterSearchResponseStopwatch.Reset();
        emotionLetterSearchState = EmotionLetterSearchState.Completed;
        emotionLetterSearchRestRemainingSeconds = 0;
        StageNoticeText = T("CaptureWorkspaceEmotionLetterSearchCompletedNotice");
        MoveToStep(CaptureWorkbenchStep.Completed);
        NotifyStageChanged();
    }

    private void StartEmotionLetterSearchTimer(int milliseconds)
    {
        emotionLetterSearchTimer.Stop();
        emotionLetterSearchTimer.Interval = TimeSpan.FromMilliseconds(milliseconds);
        emotionLetterSearchTimer.Start();
    }

    private void ResetCurrentEmotionLetterSearchTrialResult()
    {
        emotionLetterSearchResponseStopwatch.Reset();
        emotionLetterSearchHasResponded = false;
        emotionLetterSearchIsCorrect = false;
        emotionLetterSearchResponseTimeMs = null;
        emotionLetterSearchImageShownAt = null;
        emotionLetterSearchLettersShownAt = null;
        emotionLetterSearchAnsweredAt = null;
    }

    private void ResetEmotionLetterSearchState()
    {
        emotionLetterSearchTimer.Stop();
        emotionLetterSearchResponseStopwatch.Reset();
        emotionLetterSearchState = EmotionLetterSearchState.Idle;
        emotionLetterSearchIndex = 0;
        emotionLetterSearchLetterDelayMs = 0;
        emotionLetterSearchRestRemainingSeconds = 0;
        emotionLetterSearchRestAfterBlank = false;
        ResetCurrentEmotionLetterSearchTrialResult();
    }

    private void NotifyEmotionLetterSearchStateChanged()
    {
        OnPropertyChanged(nameof(IsEmotionLetterSearchFixation));
        OnPropertyChanged(nameof(IsEmotionLetterSearchImageVisible));
        OnPropertyChanged(nameof(IsEmotionLetterSearchLettersVisible));
        OnPropertyChanged(nameof(IsEmotionLetterSearchResting));
        OnPropertyChanged(nameof(ShowEmotionLetterSearchResponseButtons));
        OnPropertyChanged(nameof(EmotionLetterSearchImagePath));
        OnPropertyChanged(nameof(EmotionLetterSearchLetters));
        OnPropertyChanged(nameof(EmotionLetterSearchLetterLeft));
        OnPropertyChanged(nameof(EmotionLetterSearchLetterTop));
        OnPropertyChanged(nameof(EmotionLetterSearchRestTitleText));
        OnPropertyChanged(nameof(EmotionLetterSearchRestText));
        OnPropertyChanged(nameof(EmotionLetterSearchButtonOneText));
        OnPropertyChanged(nameof(EmotionLetterSearchButtonTwoText));
    }
}
