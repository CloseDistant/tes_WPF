namespace RuinaoSoftwareWpf;

using RuinaoSoftwareWpf.ApplicationContracts;

public sealed partial class AssessmentCaptureViewModel
{
    private void BeginVoiceBaselineSequence()
    {
        calibrationTimer.Stop();
        pictureBrowseTimer.Stop();
        videoBrowseTimer.Stop();
        voiceBaselineTimer.Stop();
        voiceBaselineIndex = 0;
        voiceBaselineRemainingSeconds = VoiceBaselineMaximumSegmentSeconds;
        currentVoiceBaselineStartedAt = null;
        voiceBaselineDetectionWindowStartedAt = null;
        voiceBaselineDetectionWindowEndedAt = null;
        voiceBaselineVoiceDetectedAt = null;
        voiceBaselineHasVoice = false;
        voiceBaselineVoiceDetectionFinalized = false;
        voiceBaselineMediaFinalizing = false;
        voiceBaselineActiveMediaSessionId = null;
        voiceBaselinePhase = VoiceBaselinePhase.WaitingToStart;
        VoiceBaselineStatusText = T("CaptureWorkspaceVoiceBaselineReady", 1, VoiceBaselineItems.Length);
        VoiceBaselineRestText = string.Empty;
        StageNoticeText = string.Empty;
    }

    private async Task StartVoiceBaselineSegmentAsync()
    {
        if (!IsVoiceBaselineModule
            || currentStep != CaptureWorkbenchStep.ModuleExecution
            || voiceBaselineIndex >= VoiceBaselineItems.Length
            || voiceBaselinePhase != VoiceBaselinePhase.WaitingToStart
            || voiceBaselineMediaFinalizing)
        {
            return;
        }

        if (!HasSelectedCamera)
        {
            StageNoticeText = T("CaptureWorkspaceNoCameraStageNotice");
            NotifyStageChanged();
            return;
        }

        var item = VoiceBaselineItems[voiceBaselineIndex];
        var startedAt = DateTimeOffset.Now;
        var sessionKey = await GetOrStartUnifiedSessionKeyAsync();
        var mediaSession = await StartMediaRecordingAsync(
            new CaptureMediaStartRequest(
                activeModuleAttempt?.AttemptId,
                sessionKey,
                CurrentModuleCode,
                CurrentModule,
                SelectedCameraDevice,
                voiceBaselineIndex + 1));

        voiceBaselineActiveMediaSessionId = mediaSession.SessionId;
        BeginFrameSaving(mediaSession.OutputDirectory);
        currentVoiceBaselineStartedAt = startedAt;
        voiceBaselineRemainingSeconds = VoiceBaselineMaximumSegmentSeconds;
        voiceBaselineDetectionWindowStartedAt = null;
        voiceBaselineDetectionWindowEndedAt = null;
        voiceBaselineVoiceDetectedAt = null;
        voiceBaselineHasVoice = false;
        voiceBaselineVoiceDetectionFinalized = false;
        voiceBaselineMediaFinalizing = false;
        voiceBaselinePhase = VoiceBaselinePhase.Preparing;
        VoiceBaselineRestText = string.Empty;
        VoiceBaselineStatusText = T("CaptureWorkspaceVoiceBaselinePreparing");
        RecordModuleEventSafely(
            "voice_baseline_segment_recording_started",
            $"语音基线第 {voiceBaselineIndex + 1} 段录制开始",
            new
            {
                segmentIndex = voiceBaselineIndex + 1,
                segmentTotal = VoiceBaselineItems.Length,
                promptText = item.PromptText,
                syllableName = item.SyllableName,
                syllableType = item.SyllableType,
                minDurationSeconds = VoiceBaselineMinimumSegmentSeconds,
                maxDurationSeconds = VoiceBaselineMaximumSegmentSeconds,
                preparationSeconds = VoiceBaselinePreparationSeconds,
                segmentRecordingStartedAtUnixMs = startedAt.ToUnixTimeMilliseconds()
            },
            startedAt,
            null);
        RecordModuleEventSafely(
            "voice_baseline_segment_started",
            $"语音基线第 {voiceBaselineIndex + 1} 段开始",
            new
            {
                segmentIndex = voiceBaselineIndex + 1,
                segmentTotal = VoiceBaselineItems.Length,
                promptText = item.PromptText,
                syllableName = item.SyllableName,
                syllableType = item.SyllableType,
                startedAtUnixMs = startedAt.ToUnixTimeMilliseconds()
            },
            startedAt,
            null);
        // Reset the shared timer so every segment gets a complete one-second
        // preparation interval, including segment 2 after the rest period.
        voiceBaselineTimer.Stop();
        voiceBaselineTimer.Start();
        NotifyStageChanged();
    }

    public async Task StartVoiceBaselineFirstSegmentAsync() => await StartVoiceBaselineSegmentAsync();

    public void StartVoiceBaselineFirstSegment() => _ = StartVoiceBaselineFirstSegmentAsync();

    public void FinishVoiceBaselineSegment()
    {
        if (CanFinishVoiceBaselineSegment)
        {
            FinishCurrentVoiceBaselineSegment(manual: true);
        }
    }

    private void AdvanceVoiceBaseline()
    {
        if (!IsVoiceBaselineModule || currentStep != CaptureWorkbenchStep.ModuleExecution)
        {
            ResetVoiceBaselineState();
            NotifyStageChanged();
            return;
        }

        if (voiceBaselinePhase == VoiceBaselinePhase.Preparing)
        {
            voiceBaselinePhase = VoiceBaselinePhase.Recording;
            voiceBaselineDetectionWindowStartedAt = DateTimeOffset.Now;
            voiceBaselineDetectionWindowEndedAt = voiceBaselineDetectionWindowStartedAt.Value.AddSeconds(VoiceBaselineVoiceDetectionWindowSeconds);
            UpdateVoiceBaselineStatusText();
            NotifyStageChanged();
            return;
        }

        if (voiceBaselinePhase == VoiceBaselinePhase.Recording)
        {
            if (voiceBaselineRemainingSeconds > 0)
            {
                voiceBaselineRemainingSeconds--;
                if (voiceBaselineRemainingSeconds == 0)
                {
                    FinishCurrentVoiceBaselineSegment(manual: false);
                    return;
                }

                if (!voiceBaselineHasVoice
                    && voiceBaselineDetectionWindowStartedAt is { } detectionStart
                    && voiceBaselineDetectionWindowEndedAt is { } detectionEnd
                    && DateTimeOffset.Now >= detectionEnd)
                {
                    voiceBaselineVoiceDetectionFinalized = true;
                    RecordModuleEventSafely(
                        "voice_baseline_voice_not_detected",
                        $"语音基线第 {voiceBaselineIndex + 1} 段检测窗口内未检测到声音",
                        new
                        {
                            segmentIndex = voiceBaselineIndex + 1,
                            voiceDetectionWindowStartedAtUnixMs = detectionStart.ToUnixTimeMilliseconds(),
                            voiceDetectionWindowEndedAtUnixMs = detectionEnd.ToUnixTimeMilliseconds(),
                            voiceDetectionThreshold = VoiceBaselineVoicePresenceRmsThreshold
                        },
                        detectionStart,
                        detectionEnd);
                }

                UpdateVoiceBaselineStatusText();
                NotifyStageChanged();
                return;
            }

            FinishCurrentVoiceBaselineSegment(manual: false);
            return;
        }

        if (voiceBaselinePhase == VoiceBaselinePhase.Resting)
        {
            if (voiceBaselineRemainingSeconds > 1)
            {
                voiceBaselineRemainingSeconds--;
                UpdateVoiceBaselineRestText();
                NotifyStageChanged();
                return;
            }

            voiceBaselinePhase = VoiceBaselinePhase.WaitingToStart;
            voiceBaselineRemainingSeconds = VoiceBaselineMaximumSegmentSeconds;
            VoiceBaselineStatusText = T("CaptureWorkspaceVoiceBaselineReady", voiceBaselineIndex + 1, VoiceBaselineItems.Length);
            VoiceBaselineRestText = string.Empty;
            var restCompletedAt = DateTimeOffset.Now;
            RecordModuleEventSafely(
                "voice_baseline_rest_completed",
                "语音基线两段之间休息结束",
                new
                {
                    nextSegmentIndex = voiceBaselineIndex + 1,
                    completedAtUnixMs = restCompletedAt.ToUnixTimeMilliseconds()
                },
                restCompletedAt,
                restCompletedAt);
            NotifyStageChanged();
        }
    }

    private void FinishCurrentVoiceBaselineSegment(bool manual)
    {
        voiceBaselineTimer.Stop();
        var completedAt = DateTimeOffset.Now;
        var item = voiceBaselineIndex >= 0 && voiceBaselineIndex < VoiceBaselineItems.Length
            ? VoiceBaselineItems[voiceBaselineIndex]
            : null;
        var durationMs = currentVoiceBaselineStartedAt.HasValue
            ? (long)(completedAt - currentVoiceBaselineStartedAt.Value).TotalMilliseconds
            : 0L;
        voiceBaselineDetectionWindowEndedAt ??= completedAt;
        StopFrameSaving();
        voiceBaselineMediaFinalizing = true;
        RecordModuleEventSafely(
            "voice_baseline_segment_recording_ended",
            $"语音基线第 {voiceBaselineIndex + 1} 段录制结束",
            new
            {
                segmentIndex = voiceBaselineIndex + 1,
                segmentTotal = VoiceBaselineItems.Length,
                segmentRecordingEndedAtUnixMs = completedAt.ToUnixTimeMilliseconds(),
                completionReason = manual ? "manual_after_minimum" : "auto_timeout"
            },
            completedAt,
            completedAt);
        if (item is not null)
        {
            RecordModuleEventSafely(
                "voice_baseline_segment_completed",
                $"语音基线第 {voiceBaselineIndex + 1} 段完成",
                new
                {
                    segmentIndex = voiceBaselineIndex + 1,
                    segmentTotal = VoiceBaselineItems.Length,
                    promptText = item.PromptText,
                    syllableName = item.SyllableName,
                    syllableType = item.SyllableType,
                    startedAtUnixMs = currentVoiceBaselineStartedAt?.ToUnixTimeMilliseconds(),
                    endedAtUnixMs = completedAt.ToUnixTimeMilliseconds(),
                    durationMs,
                    completionReason = manual ? "manual_after_minimum" : "auto_timeout",
                    hasVoice = voiceBaselineHasVoice,
                    voiceDetectedAtUnixMs = voiceBaselineVoiceDetectedAt?.ToUnixTimeMilliseconds(),
                    voiceDetectionWindowStartedAtUnixMs = voiceBaselineDetectionWindowStartedAt?.ToUnixTimeMilliseconds(),
                    voiceDetectionWindowEndedAtUnixMs = voiceBaselineDetectionWindowEndedAt?.ToUnixTimeMilliseconds(),
                    voiceDetectionThreshold = VoiceBaselineVoicePresenceRmsThreshold
                },
                currentVoiceBaselineStartedAt,
                completedAt);
        }

        RequestMediaStop(CaptureMediaStopReason.Completed, $"语音基线第 {voiceBaselineIndex + 1} 段已完成。");
        StageNoticeText = string.Empty;
        NotifyStageChanged();
    }

    private void UpdateVoiceBaselineStatusText()
    {
        VoiceBaselineStatusText = T("CaptureWorkspaceVoiceBaselineKeepSpeaking", voiceBaselineRemainingSeconds);
    }

    private void UpdateVoiceBaselineRestText()
    {
        VoiceBaselineRestText = T("CaptureWorkspaceRestRemaining", voiceBaselineRemainingSeconds);
    }

    /// <summary>
    /// 初始化词语朗读序列。
    /// 模块级音视频录制已经开始，此处只控制 6 组词语的时间戳和 UI 状态。
    /// </summary>
    private void BeginWordReadingSequence()
    {
        calibrationTimer.Stop();
        pictureBrowseTimer.Stop();
        videoBrowseTimer.Stop();
        voiceBaselineTimer.Stop();
        wordReadingTimer.Stop();
        wordReadingIndex = 0;
        wordReadingRemainingSeconds = WordReadingGroupSeconds;
        currentWordReadingStartedAt = null;
        wordReadingPhase = WordReadingPhase.WaitingToStart;
        WordReadingStatusText = T("CaptureWorkspaceWordReadingReady", 1, WordReadingGroups.Length);
        WordReadingRestText = string.Empty;
        StageNoticeText = T("CaptureWorkspaceWordReadingStageNotice");
    }

    private void StartWordReadingGroup()
    {
        if (!IsWordReadingModule || currentStep != CaptureWorkbenchStep.ModuleExecution || wordReadingIndex >= WordReadingGroups.Length)
        {
            return;
        }

        var group = WordReadingGroups[wordReadingIndex];
        currentWordReadingStartedAt = DateTimeOffset.Now;
        wordReadingRemainingSeconds = WordReadingGroupSeconds;
        wordReadingPhase = WordReadingPhase.Reading;
        WordReadingRestText = string.Empty;
        UpdateWordReadingStatusText();
        RecordModuleEventSafely(
            "word_reading_group_started",
            $"词语朗读第 {wordReadingIndex + 1} 组开始",
            new
            {
                groupIndex = wordReadingIndex + 1,
                groupTotal = WordReadingGroups.Length,
                words = group.Words,
                wordGroupType = group.WordGroupType,
                fixedDurationSeconds = WordReadingGroupSeconds,
                startedAtUnixMs = currentWordReadingStartedAt.Value.ToUnixTimeMilliseconds()
            },
            currentWordReadingStartedAt,
            null);
        wordReadingTimer.Start();
        NotifyStageChanged();
    }

    private void AdvanceWordReading()
    {
        if (!IsWordReadingModule || currentStep != CaptureWorkbenchStep.ModuleExecution)
        {
            ResetWordReadingState();
            NotifyStageChanged();
            return;
        }

        if (wordReadingPhase == WordReadingPhase.Reading)
        {
            if (wordReadingRemainingSeconds > 1)
            {
                wordReadingRemainingSeconds--;
                UpdateWordReadingStatusText();
                NotifyStageChanged();
                return;
            }

            CompleteCurrentWordReadingGroup();
            return;
        }

        if (wordReadingPhase == WordReadingPhase.Resting)
        {
            if (wordReadingRemainingSeconds > 1)
            {
                wordReadingRemainingSeconds--;
                UpdateWordReadingRestText();
                NotifyStageChanged();
                return;
            }

            StartWordReadingGroup();
        }
    }

    private void CompleteCurrentWordReadingGroup()
    {
        var completedAt = DateTimeOffset.Now;
        var group = wordReadingIndex >= 0 && wordReadingIndex < WordReadingGroups.Length
            ? WordReadingGroups[wordReadingIndex]
            : null;
        var durationMs = currentWordReadingStartedAt.HasValue
            ? (long)(completedAt - currentWordReadingStartedAt.Value).TotalMilliseconds
            : 0L;

        if (group is not null)
        {
            RecordModuleEventSafely(
                "word_reading_group_completed",
                $"词语朗读第 {wordReadingIndex + 1} 组完成",
                new
                {
                    groupIndex = wordReadingIndex + 1,
                    groupTotal = WordReadingGroups.Length,
                    words = group.Words,
                    wordGroupType = group.WordGroupType,
                    startedAtUnixMs = currentWordReadingStartedAt?.ToUnixTimeMilliseconds(),
                    endedAtUnixMs = completedAt.ToUnixTimeMilliseconds(),
                    durationMs
                },
                currentWordReadingStartedAt,
                completedAt);
        }

        wordReadingIndex++;
        currentWordReadingStartedAt = null;

        if (wordReadingIndex >= WordReadingGroups.Length)
        {
            CompleteWordReading();
            return;
        }

        wordReadingPhase = WordReadingPhase.Resting;
        wordReadingRemainingSeconds = CaptureWorkbenchForcedRestSeconds;
        WordReadingStatusText = T("CaptureWorkspaceWordReadingCompletedCount", wordReadingIndex, WordReadingGroups.Length);
        UpdateWordReadingRestText();
        NotifyStageChanged();
    }

    private void CompleteWordReading()
    {
        wordReadingTimer.Stop();
        wordReadingPhase = WordReadingPhase.Completed;
        wordReadingRemainingSeconds = 0;
        currentWordReadingStartedAt = null;
        WordReadingStatusText = T("CaptureWorkspaceWordReadingCompleted");
        WordReadingRestText = string.Empty;
        StageNoticeText = T("CaptureWorkspaceWordReadingCompletedNotice");
        MoveToStep(CaptureWorkbenchStep.Completed);
        NotifyStageChanged();
    }

    private void UpdateWordReadingStatusText()
    {
        WordReadingStatusText = T("CaptureWorkspaceWordReadingRemaining", wordReadingRemainingSeconds);
    }

    private void UpdateWordReadingRestText()
    {
        WordReadingRestText = T("CaptureWorkspaceRestRemaining", wordReadingRemainingSeconds);
    }

}
