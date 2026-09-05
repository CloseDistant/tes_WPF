namespace RuinaoSoftwareWpf;

using System.Diagnostics;
using System.Windows.Input;
using System.Windows.Threading;
using System.Text.Json;
using System.IO;

/// <summary>情绪 Stroop V3：Excel 固定序列、16 题练习和 80 题正式任务。</summary>
public sealed partial class AssessmentCaptureViewModel
{
    internal const int EmotionStroopFixationMilliseconds = 800;
    internal const int EmotionStroopStimulusMilliseconds = 2000;
    internal const int EmotionStroopPostBlankMilliseconds = 1000;
    private const int EmotionStroopRestSeconds = 30;
    private readonly DispatcherTimer emotionStroopTimer = new();
    private readonly Stopwatch emotionStroopResponseStopwatch = new();
    private EmotionStroopState emotionStroopState = EmotionStroopState.Idle;
    private IReadOnlyList<EmotionStroopTrialDefinition> emotionStroopTrials = EmotionStroopTrialCatalog.PracticeTrials;
    private int emotionStroopIndex;
    private int emotionStroopRestRemainingSeconds;
    private bool emotionStroopRestAfterBlank;
    private bool emotionStroopHasResponded;
    private bool emotionStroopIsCorrect;
    private EmotionStroopResponse? emotionStroopResponse;
    private string? emotionStroopResponseInput;
    private int emotionStroopPracticeCorrectCount;
    private long? emotionStroopResponseTimeMs;
    private DateTimeOffset? emotionStroopTargetOnset;
    private DateTimeOffset? emotionStroopAnsweredAt;
    private long emotionStroopClockAnchorTimestamp;
    private DateTimeOffset emotionStroopClockAnchorUtc;
    private long emotionStroopTargetOnsetTimestamp;
    private long emotionStroopStimulusDeadlineTimestamp;
    private long emotionStroopResponseTimestamp;
    private bool emotionStroopAwaitingRenderedOnset;
    private int? emotionStroopResponseRemainingTimeMs;
    private long? emotionStroopResponseTimeUs;
    private string emotionStroopFeedbackText = string.Empty;
    private bool emotionStroopIsPractice = true;
    private bool emotionStroopRemedial;
    private char emotionStroopVersion = 'A';
    private bool completeAfterFinalBlank;
    private bool finishPracticeAfterBlank;
    private static readonly object EmotionStroopVersionGate = new();

    public ICommand EmotionStroopRespondPositiveCommand { get; private set; } = null!;
    public ICommand EmotionStroopRespondNegativeCommand { get; private set; } = null!;
    public ICommand StartEmotionStroopPracticeCommand { get; private set; } = null!;

    public bool IsEmotionStroopFixation => IsEmotionStroopStage && emotionStroopState == EmotionStroopState.Fixation;
    public bool IsEmotionStroopStimulusVisible => IsEmotionStroopStage && emotionStroopState == EmotionStroopState.Stimulus;
    public bool IsEmotionStroopPostBlank => IsEmotionStroopStage && emotionStroopState == EmotionStroopState.PostBlank;
    public bool IsEmotionStroopResting => IsEmotionStroopStage && emotionStroopState == EmotionStroopState.Resting;
    public bool IsEmotionStroopPracticeReady => IsEmotionStroopStage && emotionStroopState == EmotionStroopState.PracticeReady;
    public bool IsEmotionStroopFormalReady => IsEmotionStroopStage && emotionStroopState == EmotionStroopState.FormalReady;
    public bool ShowEmotionStroopPracticeStartAction => IsEmotionStroopPracticeReady;
    public bool ShowEmotionStroopFormalStartAction => IsEmotionStroopFormalReady;
    public bool ShowEmotionStroopReadyPanel => IsEmotionStroopPracticeReady || IsEmotionStroopFormalReady;
    public bool CanSubmitEmotionStroopResponse => IsEmotionStroopStimulusVisible && !emotionStroopHasResponded;
    public bool ShowEmotionStroopResponseButtons => IsEmotionStroopStimulusVisible;
    public bool IsEmotionStroopFeedbackVisible => IsEmotionStroopPostBlank && emotionStroopIsPractice && !string.IsNullOrEmpty(emotionStroopFeedbackText);
    public string EmotionStroopFeedbackText => emotionStroopFeedbackText;
    public string EmotionStroopPhaseText => emotionStroopIsPractice ? (emotionStroopRemedial ? "练习未通过，请按相同顺序重做" : "练习阶段") : $"正式测试 · 版本 {emotionStroopVersion}";
    public string EmotionStroopProgressText => emotionStroopIsPractice ? $"练习 {Math.Min(emotionStroopIndex + 1, 16)}/16" : $"正式测试 {Math.Min(emotionStroopIndex + 1, 80)}/80";
    public string EmotionStroopImagePath => CurrentEmotionStroopTrial is null ? string.Empty : ResolveAssetPath("Assets", "CaptureWorkbench", "EmotionStroop", emotionStroopIsPractice ? "Practice" : "Formal", CurrentEmotionStroopTrial.ImageFileName);
    public string EmotionStroopWordText => CurrentEmotionStroopTrial?.WordText ?? string.Empty;
    public string EmotionStroopPositiveText => $"{T("CaptureWorkspaceEmotionStroopPositive")}  F";
    public string EmotionStroopNegativeText => $"{T("CaptureWorkspaceEmotionStroopNegative")}  J";
    public string EmotionStroopRestTitleText => T("CaptureWorkspaceRestTitle");
    public string EmotionStroopRestText => T("CaptureWorkspaceRestRemaining", emotionStroopRestRemainingSeconds);
    private EmotionStroopTrialDefinition? CurrentEmotionStroopTrial => emotionStroopIndex >= 0 && emotionStroopIndex < emotionStroopTrials.Count ? emotionStroopTrials[emotionStroopIndex] : null;

    private void ResetEmotionStroopClockAnchor()
    {
        emotionStroopClockAnchorTimestamp = Stopwatch.GetTimestamp();
        emotionStroopClockAnchorUtc = DateTimeOffset.UtcNow;
    }

    private DateTimeOffset TimestampToUtc(long timestamp)
    {
        if (emotionStroopClockAnchorTimestamp == 0)
        {
            ResetEmotionStroopClockAnchor();
        }

        var elapsedSeconds = (timestamp - emotionStroopClockAnchorTimestamp) / (double)Stopwatch.Frequency;
        return emotionStroopClockAnchorUtc.AddSeconds(elapsedSeconds);
    }

    private static long MillisecondsToStopwatchTicks(int milliseconds) =>
        (long)Math.Round(milliseconds * Stopwatch.Frequency / 1000d);

    private int? RemainingStimulusMilliseconds(long timestamp)
    {
        if (emotionStroopStimulusDeadlineTimestamp == 0)
        {
            return null;
        }

        var remaining = (emotionStroopStimulusDeadlineTimestamp - timestamp) * 1000d / Stopwatch.Frequency;
        return Math.Max(0, (int)Math.Round(remaining));
    }

    private void InitializeEmotionStroopModule()
    {
        EmotionStroopRespondPositiveCommand = new RelayCommand(_ => SubmitEmotionStroopResponse(EmotionStroopResponse.Positive, "click"));
        EmotionStroopRespondNegativeCommand = new RelayCommand(_ => SubmitEmotionStroopResponse(EmotionStroopResponse.Negative, "click"));
        StartEmotionStroopPracticeCommand = new RelayCommand(_ => StartEmotionStroopPractice());
        emotionStroopTimer.Tick += (_, _) => AdvanceEmotionStroop();
    }

    private void BeginEmotionStroopSequence()
    {
        StopModuleExecutionTimers();
        emotionStroopTimer.Stop();
        ResetEmotionStroopClockAnchor();
        emotionStroopTrials = EmotionStroopTrialCatalog.PracticeTrials;
        emotionStroopIndex = 0;
        emotionStroopIsPractice = true;
        emotionStroopRemedial = false;
        emotionStroopPracticeCorrectCount = 0;
        emotionStroopState = EmotionStroopState.PracticeReady;
        StageNoticeText = string.Empty;
        NotifyEmotionStroopStateChanged();
    }

    private void StartEmotionStroopPractice()
    {
        if (!IsEmotionStroopPracticeReady) return;
        emotionStroopIndex = 0;
        emotionStroopPracticeCorrectCount = 0;
        emotionStroopIsPractice = true;
        StartEmotionStroopTrial();
    }

    public void StartEmotionStroopFormal()
    {
        if (!IsEmotionStroopFormalReady) return;
        var runId = activeRun?.RunId ?? 0;
        lock (EmotionStroopVersionGate)
        {
            if (runId != 0)
            {
                var mapping = LoadEmotionStroopVersionMap();
                if (!mapping.TryGetValue(runId, out var savedVersion))
                {
                    savedVersion = mapping.Count % 2 == 0 ? 'A' : 'B';
                    mapping[runId] = savedVersion;
                    SaveEmotionStroopVersionMap(mapping);
                }
                emotionStroopVersion = savedVersion;
            }
        }
        emotionStroopTrials = EmotionStroopTrialCatalog.GetFormal(emotionStroopVersion);
        emotionStroopIndex = 0;
        emotionStroopIsPractice = false;
        completeAfterFinalBlank = false;
        StartEmotionStroopTrial();
    }

    internal void SubmitEmotionStroopKeyboardResponse(EmotionStroopResponse response) => SubmitEmotionStroopResponse(response, response == EmotionStroopResponse.Positive ? "F" : "J");

    private void StartEmotionStroopTrial()
    {
        if (!IsEmotionStroopModule || currentStep != CaptureWorkbenchStep.ModuleExecution || CurrentEmotionStroopTrial is null) return;
        ResetCurrentEmotionStroopTrialResult();
        emotionStroopState = EmotionStroopState.Fixation;
        StartEmotionStroopTimer(EmotionStroopFixationMilliseconds);
        NotifyEmotionStroopStateChanged();
    }

    private void AdvanceEmotionStroop()
    {
        emotionStroopTimer.Stop();
        if (!IsEmotionStroopModule || currentStep != CaptureWorkbenchStep.ModuleExecution) { ResetEmotionStroopState(); NotifyStageChanged(); return; }
        var nowTimestamp = Stopwatch.GetTimestamp();
        var now = TimestampToUtc(nowTimestamp);
        switch (emotionStroopState)
        {
            case EmotionStroopState.Fixation:
                emotionStroopState = EmotionStroopState.Stimulus;
                // 先用状态切换时刻兜底，下一帧由 View 的 Rendering 回调覆盖为实际提交到渲染管线的时刻。
                SetEmotionStroopStimulusTiming(nowTimestamp, restartTimer: false);
                emotionStroopAwaitingRenderedOnset = true;
                StartEmotionStroopTimer(EmotionStroopStimulusMilliseconds);
                break;
            case EmotionStroopState.Stimulus:
                emotionStroopResponseStopwatch.Stop(); CompleteCurrentEmotionStroopTrial(now); return;
            case EmotionStroopState.PostBlank:
                if (completeAfterFinalBlank) { completeAfterFinalBlank = false; CompleteEmotionStroopSequence(); return; }
                if (finishPracticeAfterBlank) { finishPracticeAfterBlank = false; FinishPracticeAttempt(); return; }
                if (emotionStroopRestAfterBlank) { emotionStroopRestAfterBlank = false; emotionStroopState = EmotionStroopState.Resting; emotionStroopRestRemainingSeconds = EmotionStroopRestSeconds; emotionStroopTimer.Interval = TimeSpan.FromSeconds(1); emotionStroopTimer.Start(); break; }
                StartEmotionStroopTrial(); return;
            case EmotionStroopState.Resting:
                if (emotionStroopRestRemainingSeconds > 1) { emotionStroopRestRemainingSeconds--; emotionStroopTimer.Interval = TimeSpan.FromSeconds(1); emotionStroopTimer.Start(); NotifyEmotionStroopStateChanged(); return; }
                emotionStroopRestRemainingSeconds = 0; StartEmotionStroopTrial(); return;
            default: return;
        }
        NotifyEmotionStroopStateChanged();
    }

    private void SetEmotionStroopStimulusTiming(long onsetTimestamp, bool restartTimer)
    {
        emotionStroopTargetOnsetTimestamp = onsetTimestamp;
        emotionStroopTargetOnset = TimestampToUtc(onsetTimestamp);
        emotionStroopStimulusDeadlineTimestamp = onsetTimestamp + MillisecondsToStopwatchTicks(EmotionStroopStimulusMilliseconds);
        emotionStroopResponseStopwatch.Restart();
        if (restartTimer)
        {
            StartEmotionStroopTimer(EmotionStroopStimulusMilliseconds);
        }
    }

    /// <summary>
    /// 由采集画面的 CompositionTarget.Rendering 回调确认刺激已经提交到 WPF 渲染管线。
    /// 状态切换时刻仍作为兜底，但正常情况下以该高精度时刻作为 targetOnset。
    /// </summary>
    internal void MarkEmotionStroopStimulusRendered()
    {
        if (emotionStroopState != EmotionStroopState.Stimulus || !emotionStroopAwaitingRenderedOnset)
        {
            return;
        }

        emotionStroopAwaitingRenderedOnset = false;
        if (emotionStroopHasResponded)
        {
            return;
        }

        SetEmotionStroopStimulusTiming(Stopwatch.GetTimestamp(), restartTimer: true);
    }

    private void SubmitEmotionStroopResponse(EmotionStroopResponse response, string input)
    {
        if (!CanSubmitEmotionStroopResponse || CurrentEmotionStroopTrial is null) return;
        var responseTimestamp = Stopwatch.GetTimestamp();
        emotionStroopResponseStopwatch.Stop(); emotionStroopHasResponded = true; emotionStroopResponseTimestamp = responseTimestamp; emotionStroopResponse = response; emotionStroopResponseInput = input; emotionStroopResponseTimeMs = emotionStroopResponseStopwatch.ElapsedMilliseconds; emotionStroopResponseTimeUs = emotionStroopTargetOnsetTimestamp == 0 ? null : (long)Math.Round((responseTimestamp - emotionStroopTargetOnsetTimestamp) * 1_000_000d / Stopwatch.Frequency); emotionStroopAnsweredAt = TimestampToUtc(responseTimestamp); emotionStroopResponseRemainingTimeMs = RemainingStimulusMilliseconds(responseTimestamp); emotionStroopIsCorrect = response == CurrentEmotionStroopTrial.CorrectResponse;
        RecordModuleEventSafely("emotion_stroop_response", $"情绪 Stroop 第 {CurrentEmotionStroopTrial.TrialIndex} 题首次有效反应", new { response = response.ToString(), input, responseTimeMs = emotionStroopResponseTimeMs, responseTimeUs = emotionStroopResponseTimeUs, responseRemainingTimeMs = emotionStroopResponseRemainingTimeMs, targetOnsetMonotonicTicks = emotionStroopTargetOnsetTimestamp, answeredAtMonotonicTicks = responseTimestamp, stopwatchFrequencyHz = Stopwatch.Frequency, isCorrect = emotionStroopIsCorrect }, emotionStroopTargetOnset, emotionStroopAnsweredAt);
        NotifyEmotionStroopStateChanged();
    }

    private void CompleteCurrentEmotionStroopTrial(DateTimeOffset completedAt)
    {
        var trial = CurrentEmotionStroopTrial; if (trial is null) return;
        // 反馈只在刺激 2000 ms 结束、进入后置空白 1000 ms 时显示，不能在响应瞬间打断刺激。
        if (emotionStroopIsPractice)
        {
            emotionStroopFeedbackText = emotionStroopHasResponded
                ? (emotionStroopIsCorrect ? "正确" : "错误")
                : "反应超时";
        }
        var targetOffset = emotionStroopStimulusDeadlineTimestamp == 0
            ? completedAt
            : TimestampToUtc(emotionStroopStimulusDeadlineTimestamp);
        var observedCompletionTimestamp = Stopwatch.GetTimestamp();
        RecordModuleEventSafely("emotion_stroop_trial_completed", $"情绪 Stroop 第 {trial.TrialIndex} 题完成", new { configurationVersion = EmotionStroopTrialCatalog.ConfigurationVersion, phase = emotionStroopIsPractice ? (emotionStroopRemedial ? "remedial" : "practice") : "formal", stimulusVersion = emotionStroopIsPractice ? null : emotionStroopVersion.ToString(), trialIndex = trial.TrialIndex, block = trial.Block, blockTrialIndex = trial.BlockTrialIndex, imageFileName = trial.ImageFileName, faceId = trial.FaceId, faceValence = trial.FaceValence, wordId = trial.WordId, wordText = trial.WordText, wordValence = trial.WordValence, condition = trial.Condition, congruency = trial.Congruency, correctResponse = trial.CorrectResponse.ToString(), response = emotionStroopResponse?.ToString(), responseInput = emotionStroopResponseInput, responseTimeMs = emotionStroopResponseTimeMs, responseTimeUs = emotionStroopResponseTimeUs, responseRemainingTimeMs = emotionStroopResponseRemainingTimeMs, isCorrect = emotionStroopHasResponded && emotionStroopIsCorrect, timeout = !emotionStroopHasResponded, targetOnsetUnixMs = UnixMilliseconds(emotionStroopTargetOnset), answeredAtUnixMs = UnixMilliseconds(emotionStroopAnsweredAt), targetOffsetUnixMs = targetOffset.ToUnixTimeMilliseconds(), targetOffsetObservedUnixMs = TimestampToUtc(observedCompletionTimestamp).ToUnixTimeMilliseconds(), targetOnsetMonotonicTicks = emotionStroopTargetOnsetTimestamp, answeredAtMonotonicTicks = emotionStroopHasResponded ? (long?)emotionStroopResponseTimestamp : null, stopwatchFrequencyHz = Stopwatch.Frequency }, emotionStroopTargetOnset, targetOffset);
        if (emotionStroopIsPractice && emotionStroopHasResponded && emotionStroopIsCorrect) emotionStroopPracticeCorrectCount++;
        emotionStroopIndex++;
        var practiceFinished = emotionStroopIsPractice && emotionStroopIndex >= emotionStroopTrials.Count;
        emotionStroopState = EmotionStroopState.PostBlank; emotionStroopRestAfterBlank = !emotionStroopIsPractice && trial.TrialIndex == 40; completeAfterFinalBlank = !emotionStroopIsPractice && emotionStroopIndex >= emotionStroopTrials.Count; finishPracticeAfterBlank = practiceFinished; StartEmotionStroopTimer(EmotionStroopPostBlankMilliseconds); NotifyEmotionStroopStateChanged();
    }

    private void FinishPracticeAttempt()
    {
        if (emotionStroopPracticeCorrectCount >= 12) { emotionStroopRemedial = false; emotionStroopState = EmotionStroopState.FormalReady; emotionStroopIndex = 0; emotionStroopFeedbackText = string.Empty; NotifyEmotionStroopStateChanged(); return; }
        emotionStroopRemedial = true; emotionStroopPracticeCorrectCount = 0; emotionStroopIndex = 0; emotionStroopFeedbackText = string.Empty; emotionStroopState = EmotionStroopState.PracticeReady; NotifyEmotionStroopStateChanged();
    }

    private void CompleteEmotionStroopSequence()
    {
        emotionStroopTimer.Stop(); emotionStroopResponseStopwatch.Reset(); emotionStroopState = EmotionStroopState.Completed; emotionStroopRestRemainingSeconds = 0; StageNoticeText = T("CaptureWorkspaceEmotionStroopCompletedNotice"); MoveToStep(CaptureWorkbenchStep.Completed); NotifyStageChanged();
    }

    private void StartEmotionStroopTimer(int milliseconds) { emotionStroopTimer.Stop(); emotionStroopTimer.Interval = TimeSpan.FromMilliseconds(milliseconds); emotionStroopTimer.Start(); }
    private void ResetCurrentEmotionStroopTrialResult() { emotionStroopResponseStopwatch.Reset(); emotionStroopHasResponded = false; emotionStroopResponseTimestamp = 0; emotionStroopResponse = null; emotionStroopResponseInput = null; emotionStroopIsCorrect = false; emotionStroopResponseTimeMs = null; emotionStroopResponseTimeUs = null; emotionStroopResponseRemainingTimeMs = null; emotionStroopTargetOnset = null; emotionStroopAnsweredAt = null; emotionStroopTargetOnsetTimestamp = 0; emotionStroopStimulusDeadlineTimestamp = 0; emotionStroopAwaitingRenderedOnset = false; emotionStroopFeedbackText = string.Empty; }
    private static Dictionary<long, char> LoadEmotionStroopVersionMap()
    {
        try
        {
            var path = EmotionStroopVersionMapPath();
            if (File.Exists(path))
            {
                var values = JsonSerializer.Deserialize<Dictionary<long, string>>(File.ReadAllText(path));
                if (values is not null) return values.ToDictionary(pair => pair.Key, pair => pair.Value == "B" ? 'B' : 'A');
            }
        }
        catch { }
        return new Dictionary<long, char>();
    }

    private static void SaveEmotionStroopVersionMap(Dictionary<long, char> mapping)
    {
        try
        {
            var path = EmotionStroopVersionMapPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(mapping.ToDictionary(pair => pair.Key, pair => pair.Value.ToString())));
        }
        catch { }
    }

    private static string EmotionStroopVersionMapPath() => Path.Combine(Path.GetDirectoryName(AppDatabasePathProvider.MainDatabasePath)!, "emotion-stroop-versions.json");
    private void ResetEmotionStroopState() { emotionStroopTimer.Stop(); emotionStroopResponseStopwatch.Reset(); emotionStroopState = EmotionStroopState.Idle; emotionStroopIndex = 0; emotionStroopRestRemainingSeconds = 0; emotionStroopRestAfterBlank = false; emotionStroopPracticeCorrectCount = 0; completeAfterFinalBlank = false; finishPracticeAfterBlank = false; ResetCurrentEmotionStroopTrialResult(); }

    private void NotifyEmotionStroopStateChanged()
    {
        OnPropertyChanged(nameof(IsEmotionStroopFixation)); OnPropertyChanged(nameof(IsEmotionStroopStimulusVisible)); OnPropertyChanged(nameof(IsEmotionStroopPostBlank)); OnPropertyChanged(nameof(IsEmotionStroopResting)); OnPropertyChanged(nameof(IsEmotionStroopPracticeReady)); OnPropertyChanged(nameof(IsEmotionStroopFormalReady)); OnPropertyChanged(nameof(ShowEmotionStroopReadyPanel)); OnPropertyChanged(nameof(ShowEmotionStroopPracticeStartAction)); OnPropertyChanged(nameof(ShowEmotionStroopFormalStartAction)); OnPropertyChanged(nameof(CanSubmitEmotionStroopResponse)); OnPropertyChanged(nameof(ShowEmotionStroopResponseButtons)); OnPropertyChanged(nameof(IsEmotionStroopFeedbackVisible)); OnPropertyChanged(nameof(EmotionStroopFeedbackText)); OnPropertyChanged(nameof(EmotionStroopPhaseText)); OnPropertyChanged(nameof(EmotionStroopProgressText)); OnPropertyChanged(nameof(EmotionStroopImagePath)); OnPropertyChanged(nameof(EmotionStroopWordText)); OnPropertyChanged(nameof(EmotionStroopPositiveText)); OnPropertyChanged(nameof(EmotionStroopNegativeText)); OnPropertyChanged(nameof(EmotionStroopRestTitleText)); OnPropertyChanged(nameof(EmotionStroopRestText));
    }
}
