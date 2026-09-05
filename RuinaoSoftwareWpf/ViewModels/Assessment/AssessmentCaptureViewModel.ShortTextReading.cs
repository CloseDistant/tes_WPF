namespace RuinaoSoftwareWpf;

using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Threading;
using RuinaoSoftwareWpf.ApplicationContracts;

/// <summary>
/// 短文朗读 V3 流程：练习不录制，正式短文只呈现一篇中性正文。
/// 正式录制从“开始正式朗读”点击时开始，点击“朗读完成”或达到 120 秒时结束。
/// </summary>
public sealed partial class AssessmentCaptureViewModel
{
    private readonly DispatcherTimer shortTextReadingTimer = new();
    private ShortTextReadingPhase shortTextReadingPhase = ShortTextReadingPhase.Idle;
    private DateTimeOffset? shortTextPhaseStartedAt;
    private DateTimeOffset? shortTextCountdownStartedAt;
    private DateTimeOffset? shortTextCountdownEndedAt;
    private DateTimeOffset? shortTextPassageStartedAt;
    private DateTimeOffset? shortTextFinishEnabledAt;
    private DateTimeOffset? shortTextPassageEndedAt;
    private DateTimeOffset? shortTextRecordingStartedAt;
    private DateTimeOffset? shortTextRecordingEndedAt;
    private DateTimeOffset? shortTextPostBlankStartedAt;
    private DateTimeOffset? shortTextPostBlankDeadline;
    private long? shortTextActiveMediaSessionId;
    private CaptureMediaCompleted? shortTextPendingMediaCompletion;
    private bool shortTextPracticeFinishEnabled;
    private string shortTextCompletionReason = string.Empty;
    private int shortTextTimeout;
    private int shortTextCountdownRemainingSeconds;
    private string shortTextReadingStatusText = string.Empty;
    private string shortTextReadingRestText = string.Empty;

    public ICommand StartShortTextReadingCommand { get; }

    public bool IsShortTextReadingWaiting => IsShortTextReadingStage
        && (shortTextReadingPhase is ShortTextReadingPhase.PracticeWaiting or ShortTextReadingPhase.FormalWaiting);

    public bool IsShortTextReadingActive => IsShortTextReadingStage
        && (shortTextReadingPhase is ShortTextReadingPhase.PracticeReading
            or ShortTextReadingPhase.FormalCountdown
            or ShortTextReadingPhase.ReadingLocked
            or ShortTextReadingPhase.ReadingSubmittable);

    public bool IsShortTextReadingCountdown => IsShortTextReadingStage
        && shortTextReadingPhase is ShortTextReadingPhase.PracticeCountdown or ShortTextReadingPhase.FormalCountdown;

    public bool IsShortTextReadingResting => false;

    public bool IsShortTextReadingPostBlank => IsShortTextReadingStage
        && shortTextReadingPhase == ShortTextReadingPhase.PostCompletionBlank;

    public bool IsShortTextReadingPromptVisible => IsShortTextReadingStage
        && (shortTextReadingPhase is not ShortTextReadingPhase.Finishing
            and not ShortTextReadingPhase.PostCompletionBlank
            and not ShortTextReadingPhase.Completed
            and not ShortTextReadingPhase.PracticeCountdown
            and not ShortTextReadingPhase.FormalCountdown);

    // Waiting pages show only the title and action. The passage itself is
    // revealed after the countdown, matching the emotion-question flow.
    public bool IsShortTextReadingContentVisible => IsShortTextReadingStage
        && shortTextReadingPhase is ShortTextReadingPhase.PracticeReading
            or ShortTextReadingPhase.ReadingLocked
            or ShortTextReadingPhase.ReadingSubmittable;

    public bool ShowShortTextReadingStartAction => IsShortTextReadingStage
        && ((shortTextReadingPhase is ShortTextReadingPhase.PracticeWaiting
                or ShortTextReadingPhase.FormalWaiting
                or ShortTextReadingPhase.ReadingLocked
                or ShortTextReadingPhase.ReadingSubmittable)
            || shortTextReadingPhase == ShortTextReadingPhase.PracticeReading);

    public bool CanExecuteShortTextReadingAction => ShowShortTextReadingStartAction
        && shortTextReadingPhase is not ShortTextReadingPhase.FormalCountdown
        and not ShortTextReadingPhase.ReadingLocked
        && (shortTextReadingPhase != ShortTextReadingPhase.PracticeReading || shortTextPracticeFinishEnabled);

    public string ShortTextReadingCountdownDisplayText => shortTextCountdownRemainingSeconds.ToString();

    public string ShortTextReadingTitleText => shortTextReadingPhase is ShortTextReadingPhase.PracticeWaiting
        or ShortTextReadingPhase.PracticeCountdown
        or ShortTextReadingPhase.PracticeReading
        ? T("CaptureWorkspaceShortTextReadingPracticeTitle")
        : T("CaptureWorkspaceShortTextReading");

    public double ShortTextReadingPassageFontSize => shortTextReadingPhase is ShortTextReadingPhase.PracticeWaiting
        or ShortTextReadingPhase.PracticeCountdown
        or ShortTextReadingPhase.PracticeReading ? 44 : 36;

    public string ShortTextReadingPassageTextAlignment =>
        shortTextReadingPhase == ShortTextReadingPhase.PracticeReading ? "Center" : "Left";

    public string ShortTextReadingStartButtonText => shortTextReadingPhase switch
    {
        ShortTextReadingPhase.PracticeWaiting => T("CaptureWorkspaceShortTextReadingStartPractice"),
        ShortTextReadingPhase.PracticeReading => T("CaptureWorkspaceShortTextReadingFinishPractice"),
        ShortTextReadingPhase.FormalWaiting => T("CaptureWorkspaceShortTextReadingStartFormal"),
        ShortTextReadingPhase.ReadingSubmittable => T("CaptureWorkspaceShortTextReadingFinish"),
        _ => T("CaptureWorkspaceShortTextReadingStartFormal")
    };

    public string ShortTextReadingPassageTitleText => shortTextReadingPhase is
        ShortTextReadingPhase.PracticeWaiting or ShortTextReadingPhase.PracticeCountdown
        or ShortTextReadingPhase.PracticeReading
        ? T("CaptureWorkspaceShortTextReadingPracticePassage")
        : T("CaptureWorkspaceShortTextReadingFormalPassage");

    public string ShortTextReadingPassageText => shortTextReadingPhase is
        ShortTextReadingPhase.PracticeWaiting or ShortTextReadingPhase.PracticeCountdown
        or ShortTextReadingPhase.PracticeReading
        ? ShortTextReadingPracticeText
        : ShortTextReadingPassages.Length == 0 ? string.Empty : ShortTextReadingPassages[0].Text;

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

    private async Task StartShortTextReadingActionAsync(CancellationToken cancellationToken)
    {
        if (!IsShortTextReadingStage || currentStep != CaptureWorkbenchStep.ModuleExecution)
        {
            return;
        }

        switch (shortTextReadingPhase)
        {
            case ShortTextReadingPhase.PracticeWaiting:
                StartShortTextPractice();
                return;
            case ShortTextReadingPhase.PracticeReading:
                CompleteShortTextPractice();
                return;
            case ShortTextReadingPhase.FormalWaiting:
                await StartShortTextFormalReadingAsync(cancellationToken);
                return;
            case ShortTextReadingPhase.ReadingSubmittable:
                FinishShortTextReading(manual: true);
                return;
        }
    }

    private void BeginShortTextReadingSequence()
    {
        calibrationTimer.Stop();
        pictureBrowseTimer.Stop();
        videoBrowseTimer.Stop();
        voiceBaselineTimer.Stop();
        wordReadingTimer.Stop();
        shortTextReadingTimer.Stop();
        ClearShortTextReadingTimingState();
        shortTextPracticeFinishEnabled = false;
        shortTextReadingPhase = ShortTextReadingPhase.PracticeWaiting;
        ShortTextReadingStatusText = T("CaptureWorkspaceShortTextReadingPracticeReady");
        ShortTextReadingRestText = string.Empty;
        StageNoticeText = T("CaptureWorkspaceShortTextReadingPracticeNotice");
        NotifyStageChanged();
    }

    private void StartShortTextPractice()
    {
        var now = DateTimeOffset.Now;
        shortTextPhaseStartedAt = now;
        shortTextCountdownStartedAt = now;
        shortTextPracticeFinishEnabled = false;
        shortTextReadingPhase = ShortTextReadingPhase.PracticeCountdown;
        shortTextCountdownRemainingSeconds = 3;
        ShortTextReadingStatusText = T("CaptureWorkspaceShortTextReadingCountdown", 3);
        RecordModuleEventSafely(
            "short_text_practice_started",
            "短文朗读练习开始",
            new
            {
                passageId = ShortTextReadingPracticePassageId,
                passageVersion = ShortTextReadingPracticePassageVersion,
                passageText = ShortTextReadingPassageText,
                countdownStartedAtUnixMs = now.ToUnixTimeMilliseconds(),
                isTest = false
            },
            now,
            null);
        RecordModuleEventSafely(
            "short_text_countdown_started",
            "短文朗读练习倒计时开始",
            new
            {
                countdownMilliseconds = ShortTextReadingCountdownMilliseconds,
                isPractice = true,
                startedAtUnixMs = now.ToUnixTimeMilliseconds()
            },
            now,
            null);
        shortTextReadingTimer.Start();
        NotifyStageChanged();
    }

    private void CompleteShortTextPractice()
    {
        if (shortTextReadingPhase != ShortTextReadingPhase.PracticeReading)
        {
            return;
        }

        var completedAt = DateTimeOffset.Now;
        RecordModuleEventSafely(
            "short_text_practice_completed",
            "短文朗读练习完成",
            new
            {
                passageId = ShortTextReadingPracticePassageId,
                passageVersion = ShortTextReadingPracticePassageVersion,
                passageText = ShortTextReadingPassageText,
                startedAtUnixMs = shortTextPassageStartedAt?.ToUnixTimeMilliseconds(),
                endedAtUnixMs = completedAt.ToUnixTimeMilliseconds(),
                durationMs = shortTextPassageStartedAt is { } started
                    ? (long)(completedAt - started).TotalMilliseconds
                    : 0L,
                isTest = false
            },
            shortTextPassageStartedAt,
            completedAt);

        shortTextReadingTimer.Stop();
        shortTextReadingPhase = ShortTextReadingPhase.FormalWaiting;
        shortTextPracticeFinishEnabled = false;
        shortTextPhaseStartedAt = null;
        shortTextCountdownStartedAt = null;
        shortTextCountdownEndedAt = null;
        shortTextPassageStartedAt = null;
        ShortTextReadingStatusText = T("CaptureWorkspaceShortTextReadingFormalReady");
        StageNoticeText = T("CaptureWorkspaceShortTextReadingFormalNotice");
        NotifyStageChanged();
    }

    private async Task StartShortTextFormalReadingAsync(CancellationToken cancellationToken)
    {
        if (shortTextReadingPhase != ShortTextReadingPhase.FormalWaiting
            || !IsShortTextReadingModule
            || currentStep != CaptureWorkbenchStep.ModuleExecution
            || shortTextActiveMediaSessionId is not null)
        {
            return;
        }

        if (!HasSelectedCamera)
        {
            StageNoticeText = T("CaptureWorkspaceNoCameraStageNotice");
            NotifyStageChanged();
            return;
        }

        var sessionKey = await GetOrStartUnifiedSessionKeyAsync(cancellationToken);
        var mediaSession = await StartMediaRecordingAsync(
            new CaptureMediaStartRequest(
                activeModuleAttempt?.AttemptId,
                sessionKey,
                CurrentModuleCode,
                CurrentModule,
                SelectedCameraDevice),
            cancellationToken);

        var startedAt = mediaSession.StartedAt;
        shortTextActiveMediaSessionId = mediaSession.SessionId;
        shortTextRecordingStartedAt = startedAt;
        shortTextPhaseStartedAt = startedAt;
        shortTextCountdownStartedAt = startedAt;
        shortTextCountdownEndedAt = null;
        shortTextPassageStartedAt = null;
        shortTextFinishEnabledAt = null;
        shortTextPassageEndedAt = null;
        shortTextRecordingEndedAt = null;
        shortTextPostBlankStartedAt = null;
        shortTextPostBlankDeadline = null;
        shortTextPendingMediaCompletion = null;
        shortTextPracticeFinishEnabled = false;
        BeginFrameSaving(mediaSession.OutputDirectory);
        shortTextReadingPhase = ShortTextReadingPhase.FormalCountdown;
        shortTextCountdownRemainingSeconds = 3;
        ShortTextReadingStatusText = T("CaptureWorkspaceShortTextReadingCountdown", 3);
        StageNoticeText = string.Empty;

        RecordModuleEventSafely(
            "short_text_recording_started",
            "短文朗读正式录制开始",
            new
            {
                passageId = ShortTextReadingPassageId,
                passageVersion = ShortTextReadingPassageVersion,
                recordingStartedAtUnixMs = startedAt.ToUnixTimeMilliseconds(),
                isTest = false
            },
            startedAt,
            null);
        RecordModuleEventSafely(
            "short_text_countdown_started",
            "短文朗读正式倒计时开始",
            new
            {
                countdownMilliseconds = ShortTextReadingCountdownMilliseconds,
                isPractice = false,
                startedAtUnixMs = startedAt.ToUnixTimeMilliseconds()
            },
            startedAt,
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

        var now = DateTimeOffset.Now;
        switch (shortTextReadingPhase)
        {
            case ShortTextReadingPhase.PracticeCountdown:
                if (ElapsedMilliseconds(shortTextCountdownStartedAt, now) >= ShortTextReadingCountdownMilliseconds)
                {
                    shortTextCountdownEndedAt = now;
                    shortTextPassageStartedAt = now;
                    shortTextPhaseStartedAt = now;
                    shortTextReadingPhase = ShortTextReadingPhase.PracticeReading;
                    ShortTextReadingStatusText = T("CaptureWorkspaceShortTextReadingPracticeKeepReading");
                    RecordModuleEventSafely(
                        "short_text_countdown_completed",
                        "短文朗读练习倒计时结束",
                        new { isPractice = true, endedAtUnixMs = now.ToUnixTimeMilliseconds() },
                        shortTextCountdownStartedAt,
                        now);
                    RecordModuleEventSafely(
                        "short_text_reading_passage_started",
                        "短文朗读练习正文出现",
                        BuildPassagePayload(true, now),
                        now,
                        null);
                }
                else
                {
                    UpdateShortTextCountdownStatus(now, shortTextCountdownStartedAt);
                }
                NotifyStageChanged();
                return;

            case ShortTextReadingPhase.PracticeReading:
                if (ElapsedMilliseconds(shortTextPassageStartedAt, now) >= ShortTextReadingPracticeMinimumMilliseconds)
                {
                    shortTextPracticeFinishEnabled = true;
                    ShortTextReadingStatusText = T("CaptureWorkspaceShortTextReadingPracticeCanFinish");
                }
                NotifyStageChanged();
                return;

            case ShortTextReadingPhase.FormalCountdown:
                if (ElapsedMilliseconds(shortTextCountdownStartedAt, now) >= ShortTextReadingCountdownMilliseconds)
                {
                    shortTextCountdownEndedAt = now;
                    shortTextPassageStartedAt = now;
                    shortTextPhaseStartedAt = now;
                    shortTextReadingPhase = ShortTextReadingPhase.ReadingLocked;
                    ShortTextReadingStatusText = T("CaptureWorkspaceShortTextReadingLocked", 5);
                    RecordModuleEventSafely(
                        "short_text_countdown_completed",
                        "短文朗读正式倒计时结束",
                        new { isPractice = false, endedAtUnixMs = now.ToUnixTimeMilliseconds() },
                        shortTextCountdownStartedAt,
                        now);
                    RecordModuleEventSafely(
                        "short_text_reading_passage_started",
                        "短文朗读正式正文出现",
                        BuildPassagePayload(false, now),
                        now,
                        null);
                }
                else
                {
                    UpdateShortTextCountdownStatus(now, shortTextCountdownStartedAt);
                }
                NotifyStageChanged();
                return;

            case ShortTextReadingPhase.ReadingLocked:
                if (ElapsedMilliseconds(shortTextPassageStartedAt, now) >= ShortTextReadingMaximumMilliseconds)
                {
                    FinishShortTextReading(manual: false);
                }
                else if (ElapsedMilliseconds(shortTextPassageStartedAt, now) >= ShortTextReadingMinimumMilliseconds)
                {
                    shortTextFinishEnabledAt = now;
                    shortTextReadingPhase = ShortTextReadingPhase.ReadingSubmittable;
                    ShortTextReadingStatusText = T("CaptureWorkspaceShortTextReadingCanFinish");
                    RecordModuleEventSafely(
                        "short_text_finish_enabled",
                        "短文朗读完成按钮已启用",
                        new { enabledAtUnixMs = now.ToUnixTimeMilliseconds(), isTest = false },
                        now,
                        null);
                }
                NotifyStageChanged();
                return;

            case ShortTextReadingPhase.ReadingSubmittable:
                if (ElapsedMilliseconds(shortTextPassageStartedAt, now) >= ShortTextReadingMaximumMilliseconds)
                {
                    FinishShortTextReading(manual: false);
                }
                else
                {
                    ShortTextReadingStatusText = T(
                        "CaptureWorkspaceShortTextReadingCanFinishWithRemaining",
                        RemainingSeconds(shortTextPassageStartedAt, now));
                    NotifyStageChanged();
                }
                return;

            case ShortTextReadingPhase.PostCompletionBlank:
                if (shortTextPostBlankDeadline is { } deadline && now >= deadline)
                {
                    shortTextReadingTimer.Stop();
                    if (shortTextPendingMediaCompletion is not null)
                    {
                        _ = CompleteShortTextReadingAfterBlankAsync();
                    }
                    else
                    {
                        shortTextReadingPhase = ShortTextReadingPhase.Finishing;
                        ShortTextReadingStatusText = T("CaptureWorkspaceShortTextReadingSaving");
                        StageNoticeText = T("CaptureWorkspaceShortTextReadingSavingNotice");
                        NotifyStageChanged();
                    }
                }
                return;
        }
    }

    private void FinishShortTextReading(bool manual)
    {
        if (!manual && shortTextReadingPhase is not ShortTextReadingPhase.ReadingLocked
            and not ShortTextReadingPhase.ReadingSubmittable)
        {
            return;
        }

        if (manual && shortTextReadingPhase != ShortTextReadingPhase.ReadingSubmittable)
        {
            return;
        }

        var endedAt = DateTimeOffset.Now;
        shortTextReadingTimer.Stop();
        shortTextPassageEndedAt = endedAt;
        shortTextRecordingEndedAt = endedAt;
        shortTextCompletionReason = manual ? "manual" : "timeout";
        shortTextTimeout = manual ? 0 : 1;
        shortTextReadingPhase = ShortTextReadingPhase.Finishing;
        StopFrameSaving();
        ShortTextReadingStatusText = T("CaptureWorkspaceShortTextReadingSaving");
        StageNoticeText = T("CaptureWorkspaceShortTextReadingSavingNotice");

        RecordModuleEventSafely(
            "short_text_reading_passage_completed",
            "短文朗读正式正文完成",
            BuildCompletionPayload(manual ? "manual" : "timeout", manual ? 0 : 1, endedAt),
            shortTextPassageStartedAt,
            endedAt);
        RecordModuleEventSafely(
            "short_text_recording_ended",
            "短文朗读正式录制结束",
            new
            {
                passageId = ShortTextReadingPassageId,
                passageVersion = ShortTextReadingPassageVersion,
                recordingEndedAtUnixMs = endedAt.ToUnixTimeMilliseconds(),
                completionReason = manual ? "manual" : "timeout",
                timeout = manual ? 0 : 1,
                isTest = false
            },
            endedAt,
            endedAt);
        BeginShortTextPostCompletionBlank(endedAt);
        RequestMediaStop(
            CaptureMediaStopReason.Completed,
            manual ? "短文朗读已手动完成。" : "短文朗读达到 120 秒上限，按超时保存。" );
        NotifyStageChanged();
    }

    private async Task<bool> HandleShortTextReadingRecordingCompletedAsync(CaptureMediaCompleted args)
    {
        if (!IsShortTextReadingModule || shortTextActiveMediaSessionId != args.Session.SessionId)
        {
            return false;
        }

        shortTextActiveMediaSessionId = null;
        if (args.Status is CaptureMediaCompletionStatus.Completed
            or CaptureMediaCompletionStatus.CompletedWithWarnings)
        {
            shortTextPendingMediaCompletion = args;
            if (shortTextReadingPhase == ShortTextReadingPhase.PostCompletionBlank)
            {
                if (shortTextPostBlankDeadline is { } deadline && DateTimeOffset.Now >= deadline)
                {
                    shortTextReadingTimer.Stop();
                    _ = CompleteShortTextReadingAfterBlankAsync();
                }
            }
            else if (shortTextReadingPhase == ShortTextReadingPhase.Finishing)
            {
                _ = CompleteShortTextReadingAfterBlankAsync();
            }
            await RunOnUiThreadAsync(NotifyStageChanged);
            return true;
        }

        var attempt = activeModuleAttempt;
        if (attempt is not null)
        {
            if (args.Status is CaptureMediaCompletionStatus.Discarded or CaptureMediaCompletionStatus.Interrupted)
            {
                await assessmentModuleLifecycle.CancelAsync(attempt.AttemptId, args.Message ?? "采集过程被用户中断。").ConfigureAwait(false);
            }
            else
            {
                await assessmentModuleLifecycle.FailAsync(
                    attempt.AttemptId,
                    args.ErrorCode ?? "MEDIA_CAPTURE_FAILED",
                    args.Message ?? "音视频保存失败。").ConfigureAwait(false);
            }

            await RunOnUiThreadAsync(() => ApplyRecordingCompletion(attempt, args));
        }
        else
        {
            await RunOnUiThreadAsync(() => ApplyDevelopmentRecordingCompletion(args));
        }

        return true;
    }

    private void BeginShortTextPostCompletionBlank(DateTimeOffset startedAt)
    {
        shortTextPostBlankStartedAt = startedAt;
        shortTextPostBlankDeadline = startedAt.AddMilliseconds(ShortTextReadingPostBlankMilliseconds);
        shortTextReadingPhase = ShortTextReadingPhase.PostCompletionBlank;
        RecordModuleEventSafely(
            "short_text_post_blank_started",
            "短文朗读结束后空白屏开始",
            new
            {
                startedAtUnixMs = startedAt.ToUnixTimeMilliseconds(),
                blankMilliseconds = ShortTextReadingPostBlankMilliseconds,
                isTest = false
            },
            startedAt,
            null);
        shortTextReadingTimer.Start();
        NotifyStageChanged();
    }

    private async Task CompleteShortTextReadingAfterBlankAsync()
    {
        var args = shortTextPendingMediaCompletion;
        if (args is null)
        {
            return;
        }

        shortTextPendingMediaCompletion = null;
        shortTextPracticeFinishEnabled = false;
        var blankEndedAt = DateTimeOffset.Now;
        RecordModuleEventSafely(
            "short_text_post_blank_completed",
            "短文朗读结束后空白屏结束",
            new
            {
                endedAtUnixMs = blankEndedAt.ToUnixTimeMilliseconds(),
                durationMs = shortTextPostBlankStartedAt is { } started
                    ? (long)(blankEndedAt - started).TotalMilliseconds
                    : ShortTextReadingPostBlankMilliseconds,
                isTest = false
            },
            shortTextPostBlankStartedAt,
            blankEndedAt);
        RecordModuleEventSafely(
            "short_text_recording_saved",
            "短文朗读音视频已保存",
            new
            {
                passageId = ShortTextReadingPassageId,
                passageVersion = ShortTextReadingPassageVersion,
                passageText = ShortTextReadingPassageText,
                passageType = ShortTextReadingPassages[0].PassageType,
                recordingStartedAtUnixMs = shortTextRecordingStartedAt?.ToUnixTimeMilliseconds(),
                countdownStartedAtUnixMs = shortTextCountdownStartedAt?.ToUnixTimeMilliseconds(),
                countdownEndedAtUnixMs = shortTextCountdownEndedAt?.ToUnixTimeMilliseconds(),
                passageStartedAtUnixMs = shortTextPassageStartedAt?.ToUnixTimeMilliseconds(),
                finishEnabledAtUnixMs = shortTextFinishEnabledAt?.ToUnixTimeMilliseconds(),
                passageEndedAtUnixMs = shortTextPassageEndedAt?.ToUnixTimeMilliseconds(),
                recordingEndedAtUnixMs = shortTextRecordingEndedAt?.ToUnixTimeMilliseconds(),
                postBlankStartedAtUnixMs = shortTextPostBlankStartedAt?.ToUnixTimeMilliseconds(),
                postBlankEndedAtUnixMs = blankEndedAt.ToUnixTimeMilliseconds(),
                recordingDurationMs = shortTextRecordingStartedAt is { } recordingStart
                    ? (long)(shortTextRecordingEndedAt.GetValueOrDefault(blankEndedAt) - recordingStart).TotalMilliseconds
                    : 0L,
                passageDurationMs = shortTextPassageStartedAt is { } passageStart
                    ? (long)(shortTextPassageEndedAt.GetValueOrDefault(blankEndedAt) - passageStart).TotalMilliseconds
                    : 0L,
                completionReason = shortTextCompletionReason,
                timeout = shortTextTimeout,
                isTest = false,
                recordingSessionId = args.Session.SessionId,
                outputDirectory = args.Session.OutputDirectory,
                completionStatus = args.Status.ToString()
            },
            shortTextRecordingStartedAt,
            blankEndedAt);

        shortTextReadingPhase = ShortTextReadingPhase.Completed;
        var attempt = activeModuleAttempt;
        if (attempt is not null)
        {
            await assessmentModuleLifecycle.CompleteAsync(
                attempt.AttemptId,
                System.Text.Json.JsonSerializer.Serialize(new
                {
                    args.Session.SessionId,
                    args.Session.OutputDirectory,
                    CompletionStatus = args.Status.ToString(),
                    args.ErrorCode,
                    args.Message,
                    passageId = ShortTextReadingPassageId,
                    passageVersion = ShortTextReadingPassageVersion,
                    completionReason = shortTextCompletionReason,
                    timeout = shortTextTimeout
                })).ConfigureAwait(false);
            await RunOnUiThreadAsync(() => ApplyRecordingCompletion(attempt, args));
        }
        else
        {
            await RunOnUiThreadAsync(() => ApplyDevelopmentRecordingCompletion(args));
        }
    }

    private object BuildPassagePayload(bool practice, DateTimeOffset startedAt)
    {
        return new
        {
            passageId = practice ? ShortTextReadingPracticePassageId : ShortTextReadingPassageId,
            passageVersion = practice ? ShortTextReadingPracticePassageVersion : ShortTextReadingPassageVersion,
            passageText = practice ? ShortTextReadingPracticeText : ShortTextReadingPassages[0].Text,
            passageType = practice ? 3 : ShortTextReadingPassages[0].PassageType,
            isPractice = practice,
            startedAtUnixMs = startedAt.ToUnixTimeMilliseconds(),
            isTest = false
        };
    }

    private object BuildCompletionPayload(string completionReason, int timeout, DateTimeOffset completedAt)
    {
        var passageStartedAt = shortTextPassageStartedAt;
        return new
        {
            passageId = ShortTextReadingPassageId,
            passageVersion = ShortTextReadingPassageVersion,
            passageText = ShortTextReadingPassageText,
            passageType = ShortTextReadingPassages[0].PassageType,
            recordingStartedAtUnixMs = shortTextRecordingStartedAt?.ToUnixTimeMilliseconds(),
            countdownStartedAtUnixMs = shortTextCountdownStartedAt?.ToUnixTimeMilliseconds(),
            countdownEndedAtUnixMs = shortTextCountdownEndedAt?.ToUnixTimeMilliseconds(),
            passageStartedAtUnixMs = passageStartedAt?.ToUnixTimeMilliseconds(),
            finishEnabledAtUnixMs = shortTextFinishEnabledAt?.ToUnixTimeMilliseconds(),
            passageEndedAtUnixMs = completedAt.ToUnixTimeMilliseconds(),
            recordingEndedAtUnixMs = shortTextRecordingEndedAt?.ToUnixTimeMilliseconds(),
            postBlankStartedAtUnixMs = shortTextPostBlankStartedAt?.ToUnixTimeMilliseconds(),
            recordingDurationMs = shortTextRecordingStartedAt is { } recordingStart
                ? (long)(completedAt - recordingStart).TotalMilliseconds
                : 0L,
            passageDurationMs = passageStartedAt is { } passageStart
                ? (long)(completedAt - passageStart).TotalMilliseconds
                : 0L,
            completionReason,
            timeout,
            isTest = false
        };
    }

    private void UpdateShortTextCountdownStatus(DateTimeOffset now, DateTimeOffset? startedAt)
    {
        var remaining = Math.Max(1, 3 - (int)(ElapsedMilliseconds(startedAt, now) / 1000));
        shortTextCountdownRemainingSeconds = remaining;
        OnPropertyChanged(nameof(ShortTextReadingCountdownDisplayText));
        ShortTextReadingStatusText = T("CaptureWorkspaceShortTextReadingCountdown", remaining);
    }

    private static long ElapsedMilliseconds(DateTimeOffset? startedAt, DateTimeOffset now) =>
        startedAt is { } start ? Math.Max(0L, (long)(now - start).TotalMilliseconds) : 0L;

    private static int RemainingSeconds(DateTimeOffset? startedAt, DateTimeOffset now)
    {
        var elapsed = ElapsedMilliseconds(startedAt, now);
        return Math.Max(0, (int)Math.Ceiling((ShortTextReadingMaximumMilliseconds - elapsed) / 1000d));
    }

    private void ResetShortTextReadingState()
    {
        shortTextReadingTimer.Stop();
        shortTextReadingPhase = ShortTextReadingPhase.Idle;
        ClearShortTextReadingTimingState();
        ShortTextReadingStatusText = T("CaptureWorkspaceRecordingPending");
        ShortTextReadingRestText = string.Empty;
    }

    private void ClearShortTextReadingTimingState()
    {
        shortTextPhaseStartedAt = null;
        shortTextCountdownStartedAt = null;
        shortTextCountdownEndedAt = null;
        shortTextPassageStartedAt = null;
        shortTextFinishEnabledAt = null;
        shortTextPassageEndedAt = null;
        shortTextRecordingStartedAt = null;
        shortTextRecordingEndedAt = null;
        shortTextPostBlankStartedAt = null;
        shortTextPostBlankDeadline = null;
        shortTextActiveMediaSessionId = null;
        shortTextPendingMediaCompletion = null;
        shortTextPracticeFinishEnabled = false;
        shortTextCompletionReason = string.Empty;
        shortTextTimeout = 0;
        shortTextCountdownRemainingSeconds = 0;
    }
}
