namespace RuinaoHardwareDebugWpf;

using System.Diagnostics;
using System.Windows.Input;
using System.Windows.Threading;

/// <summary>
/// 点探测模块流程。
/// 48 个试次按固定目录执行，每个试次依次经历前空屏、注视点、图片、后空屏和探测点阶段。
/// 模块共用一段连续音视频，试次结果通过毫秒级事件时间戳与录制文件对齐。
/// </summary>
public sealed partial class AssessmentCaptureViewModel
{
    private const int DotProbePreBlankMilliseconds = 1500;
    private const int DotProbeFixationMilliseconds = 1000;
    private const int DotProbePicturesMilliseconds = 1500;
    private const int DotProbePostBlankMilliseconds = 33;
    private const int DotProbeResponseMilliseconds = 6000;

    private readonly DispatcherTimer dotProbeTimer = new();
    private readonly Stopwatch dotProbeResponseStopwatch = new();
    private DotProbeState dotProbeState = DotProbeState.Idle;
    private int dotProbeIndex;
    private int dotProbeRestRemainingSeconds;
    private bool dotProbeHasResponded;
    private bool dotProbeIsCorrect;
    private long? dotProbeResponseTimeMs;
    private DateTimeOffset? dotProbeAnsweredAt;
    private DateTimeOffset? dotProbePreBlankStartedAt;
    private DateTimeOffset? dotProbePreBlankEndedAt;
    private DateTimeOffset? dotProbeFixationStartedAt;
    private DateTimeOffset? dotProbeFixationEndedAt;
    private DateTimeOffset? dotProbePicturesStartedAt;
    private DateTimeOffset? dotProbePicturesEndedAt;
    private DateTimeOffset? dotProbePostBlankStartedAt;
    private DateTimeOffset? dotProbePostBlankEndedAt;
    private DateTimeOffset? dotProbeProbeStartedAt;
    private DateTimeOffset? dotProbeProbeEndedAt;

    public ICommand DotProbeRespondUpCommand { get; private set; } = null!;

    public ICommand DotProbeRespondDownCommand { get; private set; } = null!;

    public bool IsDotProbePreBlank => IsDotProbeStage && dotProbeState == DotProbeState.PreBlank;

    public bool IsDotProbeFixation => IsDotProbeStage && dotProbeState == DotProbeState.Fixation;

    public bool IsDotProbePictures => IsDotProbeStage && dotProbeState == DotProbeState.Pictures;

    public bool IsDotProbePostBlank => IsDotProbeStage && dotProbeState == DotProbeState.PostBlank;

    public bool IsDotProbeProbe => IsDotProbeStage && dotProbeState == DotProbeState.Probe;

    public bool IsDotProbeResting => IsDotProbeStage && dotProbeState == DotProbeState.Resting;

    public bool IsDotProbeProbeTop => IsDotProbeProbe && CurrentDotProbeTrial?.ProbePosition == DotProbePosition.Top;

    public bool IsDotProbeProbeBottom => IsDotProbeProbe && CurrentDotProbeTrial?.ProbePosition == DotProbePosition.Bottom;

    public bool ShowDotProbeResponseButtons => IsDotProbeProbe && !dotProbeHasResponded;

    public string DotProbeTopImagePath => CurrentDotProbeTrial is null
        ? string.Empty
        : ResolveAssetPath("Assets", "CaptureWorkbench", "DotProbe", CurrentDotProbeTrial.TopImageFileName);

    public string DotProbeBottomImagePath => CurrentDotProbeTrial is null
        ? string.Empty
        : ResolveAssetPath("Assets", "CaptureWorkbench", "DotProbe", CurrentDotProbeTrial.BottomImageFileName);

    public string DotProbeRestTitleText => T("CaptureWorkspaceDotProbeRestTitle");

    public string DotProbeRestText => T("CaptureWorkspaceRestRemaining", dotProbeRestRemainingSeconds);

    public string DotProbeUpText => T("CaptureWorkspaceDotProbeUp");

    public string DotProbeDownText => T("CaptureWorkspaceDotProbeDown");

    private DotProbeTrialDefinition? CurrentDotProbeTrial => dotProbeIndex >= 0
        && dotProbeIndex < DotProbeTrialCatalog.Trials.Count
            ? DotProbeTrialCatalog.Trials[dotProbeIndex]
            : null;

    private void InitializeDotProbeModule()
    {
        DotProbeRespondUpCommand = new RelayCommand(_ => SubmitDotProbeResponse(DotProbeResponse.Up));
        DotProbeRespondDownCommand = new RelayCommand(_ => SubmitDotProbeResponse(DotProbeResponse.Down));
        dotProbeTimer.Tick += (_, _) => AdvanceDotProbe();
    }

    private void BeginDotProbeSequence()
    {
        calibrationTimer.Stop();
        pictureBrowseTimer.Stop();
        videoBrowseTimer.Stop();
        voiceBaselineTimer.Stop();
        wordReadingTimer.Stop();
        shortTextReadingTimer.Stop();
        emotionQuestionTimer.Stop();
        ResetDotProbeState();

        dotProbeIndex = 0;
        StageNoticeText = string.Empty;
        StartDotProbeTrial();
    }

    private void StartDotProbeTrial()
    {
        if (!IsDotProbeModule
            || currentStep != CaptureWorkbenchStep.ModuleExecution
            || CurrentDotProbeTrial is null)
        {
            return;
        }

        ResetCurrentDotProbeTrialResult();
        dotProbeState = DotProbeState.PreBlank;
        dotProbePreBlankStartedAt = DateTimeOffset.Now;
        StartDotProbeTimer(DotProbePreBlankMilliseconds);
        NotifyStageChanged();
    }

    private void AdvanceDotProbe()
    {
        dotProbeTimer.Stop();
        if (!IsDotProbeModule || currentStep != CaptureWorkbenchStep.ModuleExecution)
        {
            ResetDotProbeState();
            NotifyStageChanged();
            return;
        }

        var now = DateTimeOffset.Now;
        switch (dotProbeState)
        {
            case DotProbeState.PreBlank:
                dotProbePreBlankEndedAt = now;
                dotProbeFixationStartedAt = now;
                dotProbeState = DotProbeState.Fixation;
                StartDotProbeTimer(DotProbeFixationMilliseconds);
                break;
            case DotProbeState.Fixation:
                dotProbeFixationEndedAt = now;
                dotProbePicturesStartedAt = now;
                dotProbeState = DotProbeState.Pictures;
                StartDotProbeTimer(DotProbePicturesMilliseconds);
                break;
            case DotProbeState.Pictures:
                dotProbePicturesEndedAt = now;
                dotProbePostBlankStartedAt = now;
                dotProbeState = DotProbeState.PostBlank;
                StartDotProbeTimer(DotProbePostBlankMilliseconds);
                break;
            case DotProbeState.PostBlank:
                dotProbePostBlankEndedAt = now;
                dotProbeProbeStartedAt = now;
                dotProbeState = DotProbeState.Probe;
                dotProbeResponseStopwatch.Restart();
                StartDotProbeTimer(DotProbeResponseMilliseconds);
                break;
            case DotProbeState.Probe:
                dotProbeResponseStopwatch.Stop();
                dotProbeProbeEndedAt = now;
                CompleteCurrentDotProbeTrial(now);
                return;
            case DotProbeState.Resting:
                AdvanceDotProbeRest();
                return;
            default:
                return;
        }

        NotifyStageChanged();
    }

    private void SubmitDotProbeResponse(DotProbeResponse response)
    {
        if (!IsDotProbeProbe || dotProbeHasResponded || CurrentDotProbeTrial is null)
        {
            return;
        }

        dotProbeResponseStopwatch.Stop();
        dotProbeHasResponded = true;
        dotProbeResponseTimeMs = dotProbeResponseStopwatch.ElapsedMilliseconds;
        dotProbeAnsweredAt = DateTimeOffset.Now;
        dotProbeIsCorrect = response == CurrentDotProbeTrial.CorrectResponse;
        NotifyStageChanged();
    }

    private void CompleteCurrentDotProbeTrial(DateTimeOffset completedAt)
    {
        var trial = CurrentDotProbeTrial;
        if (trial is null)
        {
            return;
        }

        RecordModuleEventSafely(
            "dot_probe_trial_completed",
            $"点探测第 {trial.TrialIndex} 试次完成",
            new
            {
                configurationVersion = DotProbeTrialCatalog.ConfigurationVersion,
                trialIndex = trial.TrialIndex,
                isCorrect = dotProbeHasResponded && dotProbeIsCorrect,
                responseTimeMs = dotProbeResponseTimeMs,
                preBlankStartedAtUnixMs = UnixMilliseconds(dotProbePreBlankStartedAt),
                preBlankEndedAtUnixMs = UnixMilliseconds(dotProbePreBlankEndedAt),
                fixationStartedAtUnixMs = UnixMilliseconds(dotProbeFixationStartedAt),
                fixationEndedAtUnixMs = UnixMilliseconds(dotProbeFixationEndedAt),
                picturesStartedAtUnixMs = UnixMilliseconds(dotProbePicturesStartedAt),
                picturesEndedAtUnixMs = UnixMilliseconds(dotProbePicturesEndedAt),
                postBlankStartedAtUnixMs = UnixMilliseconds(dotProbePostBlankStartedAt),
                postBlankEndedAtUnixMs = UnixMilliseconds(dotProbePostBlankEndedAt),
                probeStartedAtUnixMs = UnixMilliseconds(dotProbeProbeStartedAt),
                probeEndedAtUnixMs = UnixMilliseconds(dotProbeProbeEndedAt),
                answeredAtUnixMs = UnixMilliseconds(dotProbeAnsweredAt),
                trialEndedAtUnixMs = completedAt.ToUnixTimeMilliseconds()
            },
            dotProbePreBlankStartedAt,
            completedAt);

        dotProbeIndex++;
        if (dotProbeIndex >= DotProbeTrialCatalog.Trials.Count)
        {
            CompleteDotProbeSequence();
            return;
        }

        if (trial.TrialIndex is 18 or 36)
        {
            dotProbeState = DotProbeState.Resting;
            dotProbeRestRemainingSeconds = CaptureWorkbenchForcedRestSeconds;
            dotProbeTimer.Interval = TimeSpan.FromSeconds(1);
            dotProbeTimer.Start();
            NotifyStageChanged();
            return;
        }

        StartDotProbeTrial();
    }

    private void AdvanceDotProbeRest()
    {
        if (dotProbeRestRemainingSeconds > 1)
        {
            dotProbeRestRemainingSeconds--;
            dotProbeTimer.Interval = TimeSpan.FromSeconds(1);
            dotProbeTimer.Start();
            NotifyStageChanged();
            return;
        }

        dotProbeRestRemainingSeconds = 0;
        StartDotProbeTrial();
    }

    private void CompleteDotProbeSequence()
    {
        dotProbeTimer.Stop();
        dotProbeResponseStopwatch.Reset();
        dotProbeState = DotProbeState.Completed;
        dotProbeRestRemainingSeconds = 0;
        StageNoticeText = T("CaptureWorkspaceDotProbeCompletedNotice");
        MoveToStep(CaptureWorkbenchStep.Completed);
        NotifyStageChanged();
    }

    private void StartDotProbeTimer(int milliseconds)
    {
        dotProbeTimer.Stop();
        dotProbeTimer.Interval = TimeSpan.FromMilliseconds(milliseconds);
        dotProbeTimer.Start();
    }

    private void ResetCurrentDotProbeTrialResult()
    {
        dotProbeResponseStopwatch.Reset();
        dotProbeHasResponded = false;
        dotProbeIsCorrect = false;
        dotProbeResponseTimeMs = null;
        dotProbeAnsweredAt = null;
        dotProbePreBlankStartedAt = null;
        dotProbePreBlankEndedAt = null;
        dotProbeFixationStartedAt = null;
        dotProbeFixationEndedAt = null;
        dotProbePicturesStartedAt = null;
        dotProbePicturesEndedAt = null;
        dotProbePostBlankStartedAt = null;
        dotProbePostBlankEndedAt = null;
        dotProbeProbeStartedAt = null;
        dotProbeProbeEndedAt = null;
    }

    private void ResetDotProbeState()
    {
        dotProbeTimer.Stop();
        dotProbeResponseStopwatch.Reset();
        dotProbeState = DotProbeState.Idle;
        dotProbeIndex = 0;
        dotProbeRestRemainingSeconds = 0;
        ResetCurrentDotProbeTrialResult();
    }

    private static long? UnixMilliseconds(DateTimeOffset? value)
        => value?.ToUnixTimeMilliseconds();
}
