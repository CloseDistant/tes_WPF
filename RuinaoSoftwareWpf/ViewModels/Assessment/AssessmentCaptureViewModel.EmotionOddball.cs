namespace RuinaoSoftwareWpf;

using System.Diagnostics;
using System.Windows.Input;
using System.Windows.Threading;

/// <summary>
/// 情绪 Oddball 模块流程。
/// 64 个固定试次共用一段连续音视频，逐试次事件通过毫秒时间戳与录制文件对齐。
/// </summary>
public sealed partial class AssessmentCaptureViewModel
{
    private const int EmotionOddballFixationMilliseconds = 850;
    private const int EmotionOddballShapeDelayMilliseconds = 300;
    private const int EmotionOddballImageMilliseconds = 6000;
    private const int EmotionOddballPostBlankMilliseconds = 1250;

    private readonly DispatcherTimer emotionOddballTimer = new();
    private readonly Stopwatch emotionOddballResponseStopwatch = new();
    private EmotionOddballState emotionOddballState = EmotionOddballState.Idle;
    private int emotionOddballIndex;
    private bool emotionOddballHasResponded;
    private bool emotionOddballIsCorrect;
    private long? emotionOddballResponseTimeMs;
    private DateTimeOffset? emotionOddballImageShownAt;
    private DateTimeOffset? emotionOddballShapeShownAt;
    private DateTimeOffset? emotionOddballAnsweredAt;

    public ICommand EmotionOddballRespondSquareCommand { get; private set; } = null!;

    public ICommand EmotionOddballRespondCircleCommand { get; private set; } = null!;

    public bool IsEmotionOddballFixation => IsEmotionOddballStage && emotionOddballState == EmotionOddballState.Fixation;

    public bool IsEmotionOddballImageVisible => IsEmotionOddballStage
        && emotionOddballState is EmotionOddballState.ImageOnly or EmotionOddballState.ImageWithShape;

    public bool IsEmotionOddballShapeVisible => IsEmotionOddballStage
        && emotionOddballState == EmotionOddballState.ImageWithShape;

    public bool IsEmotionOddballCircle => IsEmotionOddballShapeVisible
        && CurrentEmotionOddballTrial?.Shape == EmotionOddballShape.Circle;

    public bool IsEmotionOddballSquare => IsEmotionOddballShapeVisible
        && CurrentEmotionOddballTrial?.Shape == EmotionOddballShape.Square;

    public bool ShowEmotionOddballResponseButtons => IsEmotionOddballShapeVisible && !emotionOddballHasResponded;

    public string EmotionOddballImagePath => CurrentEmotionOddballTrial is null
        ? string.Empty
        : ResolveAssetPath("Assets", "CaptureWorkbench", "EmotionOddball", CurrentEmotionOddballTrial.ImageFileName);

    public string EmotionOddballProgressText => T(
        "CaptureWorkspaceEmotionOddballProgress",
        Math.Min(emotionOddballIndex + 1, EmotionOddballTrialCatalog.Trials.Count),
        EmotionOddballTrialCatalog.Trials.Count);

    public string EmotionOddballSquareText => T("CaptureWorkspaceEmotionOddballSquare");

    public string EmotionOddballCircleText => T("CaptureWorkspaceEmotionOddballCircle");

    private EmotionOddballTrialDefinition? CurrentEmotionOddballTrial => emotionOddballIndex >= 0
        && emotionOddballIndex < EmotionOddballTrialCatalog.Trials.Count
            ? EmotionOddballTrialCatalog.Trials[emotionOddballIndex]
            : null;

    private void InitializeEmotionOddballModule()
    {
        EmotionOddballRespondSquareCommand = new RelayCommand(_ => SubmitEmotionOddballResponse(EmotionOddballResponse.Square));
        EmotionOddballRespondCircleCommand = new RelayCommand(_ => SubmitEmotionOddballResponse(EmotionOddballResponse.Circle));
        emotionOddballTimer.Tick += (_, _) => AdvanceEmotionOddball();
    }

    private void BeginEmotionOddballSequence()
    {
        StopModuleExecutionTimers();
        ResetEmotionOddballState();
        emotionOddballIndex = 0;
        StageNoticeText = string.Empty;
        StartEmotionOddballTrial();
    }

    private void StartEmotionOddballTrial()
    {
        if (!IsEmotionOddballModule
            || currentStep != CaptureWorkbenchStep.ModuleExecution
            || CurrentEmotionOddballTrial is null)
        {
            return;
        }

        ResetCurrentEmotionOddballTrialResult();
        emotionOddballState = EmotionOddballState.Fixation;
        StartEmotionOddballTimer(EmotionOddballFixationMilliseconds);
        NotifyStageChanged();
    }

    private void AdvanceEmotionOddball()
    {
        emotionOddballTimer.Stop();
        if (!IsEmotionOddballModule || currentStep != CaptureWorkbenchStep.ModuleExecution)
        {
            ResetEmotionOddballState();
            NotifyStageChanged();
            return;
        }

        var now = DateTimeOffset.Now;
        switch (emotionOddballState)
        {
            case EmotionOddballState.Fixation:
                emotionOddballState = EmotionOddballState.ImageOnly;
                emotionOddballImageShownAt = now;
                emotionOddballResponseStopwatch.Restart();
                StartEmotionOddballTimer(EmotionOddballShapeDelayMilliseconds);
                break;
            case EmotionOddballState.ImageOnly:
                emotionOddballState = EmotionOddballState.ImageWithShape;
                emotionOddballShapeShownAt = now;
                StartEmotionOddballTimer(EmotionOddballImageMilliseconds - EmotionOddballShapeDelayMilliseconds);
                break;
            case EmotionOddballState.ImageWithShape:
                emotionOddballResponseStopwatch.Stop();
                CompleteCurrentEmotionOddballTrial(now);
                return;
            case EmotionOddballState.PostBlank:
                StartEmotionOddballTrial();
                return;
            default:
                return;
        }

        NotifyEmotionOddballStateChanged();
    }

    private void SubmitEmotionOddballResponse(EmotionOddballResponse response)
    {
        if (!IsEmotionOddballShapeVisible || emotionOddballHasResponded || CurrentEmotionOddballTrial is null)
        {
            return;
        }

        emotionOddballHasResponded = true;
        emotionOddballResponseTimeMs = emotionOddballResponseStopwatch.ElapsedMilliseconds;
        emotionOddballAnsweredAt = DateTimeOffset.Now;
        emotionOddballIsCorrect = response == CurrentEmotionOddballTrial.CorrectResponse;
        NotifyEmotionOddballStateChanged();
    }

    private void CompleteCurrentEmotionOddballTrial(DateTimeOffset completedAt)
    {
        var trial = CurrentEmotionOddballTrial;
        if (trial is null)
        {
            return;
        }

        RecordModuleEventSafely(
            "emotion_oddball_trial_completed",
            $"情绪 Oddball 第 {trial.TrialIndex} 试次完成",
            new
            {
                configurationVersion = EmotionOddballTrialCatalog.ConfigurationVersion,
                trialIndex = trial.TrialIndex,
                imageShownAtUnixMs = UnixMilliseconds(emotionOddballImageShownAt),
                shapeShownAtUnixMs = UnixMilliseconds(emotionOddballShapeShownAt),
                answeredAtUnixMs = UnixMilliseconds(emotionOddballAnsweredAt),
                responseTimeMs = emotionOddballResponseTimeMs,
                isCorrect = emotionOddballHasResponded && emotionOddballIsCorrect,
                trialEndedAtUnixMs = completedAt.ToUnixTimeMilliseconds()
            },
            emotionOddballImageShownAt,
            completedAt);

        emotionOddballIndex++;
        if (emotionOddballIndex >= EmotionOddballTrialCatalog.Trials.Count)
        {
            CompleteEmotionOddballSequence();
            return;
        }

        emotionOddballState = EmotionOddballState.PostBlank;
        StartEmotionOddballTimer(EmotionOddballPostBlankMilliseconds);
        NotifyEmotionOddballStateChanged();
    }

    private void CompleteEmotionOddballSequence()
    {
        emotionOddballTimer.Stop();
        emotionOddballResponseStopwatch.Reset();
        emotionOddballState = EmotionOddballState.Completed;
        StageNoticeText = T("CaptureWorkspaceEmotionOddballCompletedNotice");
        MoveToStep(CaptureWorkbenchStep.Completed);
        NotifyStageChanged();
    }

    private void StartEmotionOddballTimer(int milliseconds)
    {
        emotionOddballTimer.Stop();
        emotionOddballTimer.Interval = TimeSpan.FromMilliseconds(milliseconds);
        emotionOddballTimer.Start();
    }

    private void ResetCurrentEmotionOddballTrialResult()
    {
        emotionOddballResponseStopwatch.Reset();
        emotionOddballHasResponded = false;
        emotionOddballIsCorrect = false;
        emotionOddballResponseTimeMs = null;
        emotionOddballImageShownAt = null;
        emotionOddballShapeShownAt = null;
        emotionOddballAnsweredAt = null;
    }

    private void ResetEmotionOddballState()
    {
        emotionOddballTimer.Stop();
        emotionOddballResponseStopwatch.Reset();
        emotionOddballState = EmotionOddballState.Idle;
        emotionOddballIndex = 0;
        ResetCurrentEmotionOddballTrialResult();
    }

    private void NotifyEmotionOddballStateChanged()
    {
        OnPropertyChanged(nameof(IsEmotionOddballFixation));
        OnPropertyChanged(nameof(IsEmotionOddballImageVisible));
        OnPropertyChanged(nameof(IsEmotionOddballShapeVisible));
        OnPropertyChanged(nameof(IsEmotionOddballCircle));
        OnPropertyChanged(nameof(IsEmotionOddballSquare));
        OnPropertyChanged(nameof(ShowEmotionOddballResponseButtons));
        OnPropertyChanged(nameof(EmotionOddballImagePath));
        OnPropertyChanged(nameof(EmotionOddballProgressText));
        OnPropertyChanged(nameof(EmotionOddballSquareText));
        OnPropertyChanged(nameof(EmotionOddballCircleText));
    }
}
