namespace RuinaoSoftwareWpf;

using System.Windows.Input;
using System.Windows.Threading;

/// <summary>
/// 短文朗读模块流程。
/// 第一段由用户手动开始，之后按“朗读 30 秒、休息 12 秒”自动推进。
/// 整个模块共用一段音视频，每段短文通过毫秒级事件时间戳定位。
/// </summary>
public sealed partial class AssessmentCaptureViewModel
{
    private readonly DispatcherTimer shortTextReadingTimer = new();
    private ShortTextReadingPhase shortTextReadingPhase = ShortTextReadingPhase.Idle;
    private int shortTextReadingIndex;
    private int shortTextReadingRemainingSeconds;
    private DateTimeOffset? currentShortTextReadingStartedAt;
    private string shortTextReadingStatusText = string.Empty;
    private string shortTextReadingRestText = string.Empty;

    public ICommand StartShortTextReadingCommand { get; }

    public bool IsShortTextReadingWaiting => IsShortTextReadingStage
        && shortTextReadingPhase == ShortTextReadingPhase.WaitingToStart;

    public bool IsShortTextReadingActive => IsShortTextReadingStage
        && shortTextReadingPhase == ShortTextReadingPhase.Reading;

    public bool IsShortTextReadingResting => IsShortTextReadingStage
        && shortTextReadingPhase == ShortTextReadingPhase.Resting;

    public bool IsShortTextReadingPromptVisible => IsShortTextReadingStage
        && shortTextReadingPhase != ShortTextReadingPhase.Resting;

    public bool ShowShortTextReadingStartAction => IsShortTextReadingWaiting && shortTextReadingIndex == 0;

    public string ShortTextReadingTitleText => T("CaptureWorkspaceShortTextReading");

    public string ShortTextReadingStartButtonText => T("CaptureWorkspaceShortTextReadingStart");

    public string ShortTextReadingPassageTitleText => T(
        "CaptureWorkspaceShortTextReadingPassage",
        shortTextReadingIndex + 1,
        ShortTextReadingPassages.Length);

    public string ShortTextReadingPassageText => shortTextReadingIndex >= 0
        && shortTextReadingIndex < ShortTextReadingPassages.Length
            ? ShortTextReadingPassages[shortTextReadingIndex].Text
            : string.Empty;

    public string ShortTextReadingStatusText
    {
        get => shortTextReadingStatusText;
        private set => SetProperty(ref shortTextReadingStatusText, value);
    }

    public string ShortTextReadingRestText
    {
        get => shortTextReadingRestText;
        private set => SetProperty(ref shortTextReadingRestText, value);
    }

    /// <summary>
    /// 第一段短文由用户手动点击开始，后续段落由固定休息倒计时自动推进。
    /// </summary>
    private void StartShortTextReadingFirstPassage()
    {
        if (!IsShortTextReadingWaiting || shortTextReadingIndex != 0)
        {
            return;
        }

        StartShortTextReadingPassage();
    }

    private void BeginShortTextReadingSequence()
    {
        calibrationTimer.Stop();
        pictureBrowseTimer.Stop();
        videoBrowseTimer.Stop();
        voiceBaselineTimer.Stop();
        wordReadingTimer.Stop();
        shortTextReadingTimer.Stop();

        shortTextReadingIndex = 0;
        shortTextReadingRemainingSeconds = ShortTextReadingPassageSeconds;
        currentShortTextReadingStartedAt = null;
        shortTextReadingPhase = ShortTextReadingPhase.WaitingToStart;
        ShortTextReadingStatusText = T(
            "CaptureWorkspaceShortTextReadingReady",
            1,
            ShortTextReadingPassages.Length);
        ShortTextReadingRestText = string.Empty;
        StageNoticeText = T("CaptureWorkspaceShortTextReadingStageNotice");
    }

    private void StartShortTextReadingPassage()
    {
        if (!IsShortTextReadingModule
            || currentStep != CaptureWorkbenchStep.ModuleExecution
            || shortTextReadingIndex >= ShortTextReadingPassages.Length)
        {
            return;
        }

        var passage = ShortTextReadingPassages[shortTextReadingIndex];
        currentShortTextReadingStartedAt = DateTimeOffset.Now;
        shortTextReadingRemainingSeconds = ShortTextReadingPassageSeconds;
        shortTextReadingPhase = ShortTextReadingPhase.Reading;
        StageNoticeText = string.Empty;
        ShortTextReadingRestText = string.Empty;
        UpdateShortTextReadingStatusText();

        RecordModuleEventSafely(
            "short_text_reading_passage_started",
            $"短文朗读第 {shortTextReadingIndex + 1} 段开始",
            new
            {
                passageIndex = shortTextReadingIndex + 1,
                passageTotal = ShortTextReadingPassages.Length,
                passageText = passage.Text,
                passageType = passage.PassageType,
                fixedDurationSeconds = ShortTextReadingPassageSeconds,
                startedAtUnixMs = currentShortTextReadingStartedAt.Value.ToUnixTimeMilliseconds()
            },
            currentShortTextReadingStartedAt,
            null);

        shortTextReadingTimer.Start();
        NotifyStageChanged();
    }

    private void AdvanceShortTextReading()
    {
        if (!IsShortTextReadingModule || currentStep != CaptureWorkbenchStep.ModuleExecution)
        {
            ResetShortTextReadingState();
            NotifyStageChanged();
            return;
        }

        if (shortTextReadingPhase == ShortTextReadingPhase.Reading)
        {
            if (shortTextReadingRemainingSeconds > 1)
            {
                shortTextReadingRemainingSeconds--;
                UpdateShortTextReadingStatusText();
                NotifyStageChanged();
                return;
            }

            CompleteCurrentShortTextReadingPassage();
            return;
        }

        if (shortTextReadingPhase != ShortTextReadingPhase.Resting)
        {
            return;
        }

        if (shortTextReadingRemainingSeconds > 1)
        {
            shortTextReadingRemainingSeconds--;
            UpdateShortTextReadingRestText();
            NotifyStageChanged();
            return;
        }

        StartShortTextReadingPassage();
    }

    private void CompleteCurrentShortTextReadingPassage()
    {
        var completedAt = DateTimeOffset.Now;
        var passage = shortTextReadingIndex >= 0 && shortTextReadingIndex < ShortTextReadingPassages.Length
            ? ShortTextReadingPassages[shortTextReadingIndex]
            : null;
        var durationMs = currentShortTextReadingStartedAt.HasValue
            ? (long)(completedAt - currentShortTextReadingStartedAt.Value).TotalMilliseconds
            : 0L;

        if (passage is not null)
        {
            RecordModuleEventSafely(
                "short_text_reading_passage_completed",
                $"短文朗读第 {shortTextReadingIndex + 1} 段完成",
                new
                {
                    passageIndex = shortTextReadingIndex + 1,
                    passageTotal = ShortTextReadingPassages.Length,
                    passageText = passage.Text,
                    passageType = passage.PassageType,
                    startedAtUnixMs = currentShortTextReadingStartedAt?.ToUnixTimeMilliseconds(),
                    endedAtUnixMs = completedAt.ToUnixTimeMilliseconds(),
                    durationMs
                },
                currentShortTextReadingStartedAt,
                completedAt);
        }

        shortTextReadingIndex++;
        currentShortTextReadingStartedAt = null;

        if (shortTextReadingIndex >= ShortTextReadingPassages.Length)
        {
            CompleteShortTextReading();
            return;
        }

        shortTextReadingPhase = ShortTextReadingPhase.Resting;
        shortTextReadingRemainingSeconds = CaptureWorkbenchForcedRestSeconds;
        ShortTextReadingStatusText = T(
            "CaptureWorkspaceShortTextReadingCompletedCount",
            shortTextReadingIndex,
            ShortTextReadingPassages.Length);
        UpdateShortTextReadingRestText();
        NotifyStageChanged();
    }

    private void CompleteShortTextReading()
    {
        shortTextReadingTimer.Stop();
        shortTextReadingPhase = ShortTextReadingPhase.Completed;
        shortTextReadingRemainingSeconds = 0;
        currentShortTextReadingStartedAt = null;
        ShortTextReadingStatusText = T("CaptureWorkspaceShortTextReadingCompleted");
        ShortTextReadingRestText = string.Empty;
        StageNoticeText = T("CaptureWorkspaceShortTextReadingCompletedNotice");
        MoveToStep(CaptureWorkbenchStep.Completed);
        NotifyStageChanged();
    }

    private void UpdateShortTextReadingStatusText()
    {
        ShortTextReadingStatusText = T(
            "CaptureWorkspaceShortTextReadingRemaining",
            shortTextReadingRemainingSeconds);
    }

    private void UpdateShortTextReadingRestText()
    {
        ShortTextReadingRestText = T(
            "CaptureWorkspaceRestRemaining",
            shortTextReadingRemainingSeconds);
    }

    private void ResetShortTextReadingState()
    {
        shortTextReadingTimer.Stop();
        shortTextReadingPhase = ShortTextReadingPhase.Idle;
        shortTextReadingIndex = 0;
        shortTextReadingRemainingSeconds = ShortTextReadingPassageSeconds;
        currentShortTextReadingStartedAt = null;
        ShortTextReadingStatusText = T("CaptureWorkspaceRecordingPending");
        ShortTextReadingRestText = string.Empty;
    }
}
