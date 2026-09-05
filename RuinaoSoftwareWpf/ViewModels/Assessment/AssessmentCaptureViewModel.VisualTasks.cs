namespace RuinaoSoftwareWpf;

using System.IO;

public sealed partial class AssessmentCaptureViewModel
{
    private void BeginCalibrationSequence()
    {
        pictureBrowseTimer.Stop();
        ResetCalibrationSequence();

        foreach (var frame in calibrationSequenceFactory.Create())
        {
            calibrationFrames.Enqueue(frame);
        }

        ShowNextCalibrationFrame();
    }

    /// <summary>
    /// 推进眼动校准点序列。
    /// 队列为空时表示本模块第三步完成，进入模块完成阶段。
    /// </summary>
    private void ShowNextCalibrationFrame()
    {
        calibrationTimer.Stop();
        var now = timeProvider.GetUtcNow();
        CompleteCalibrationPoint(now);
        if (calibrationFrames.Count == 0)
        {
            IsCalibrationMarkerVisible = false;
            CalibrationAnimationSequence++;

            MoveToStep(CaptureWorkbenchStep.Completed);
            NotifyStageChanged();
            return;
        }

        var frame = calibrationFrames.Dequeue();
        if (calibrationTrialIndex != frame.TrialIndex)
        {
            calibrationTrialIndex = frame.TrialIndex;
            OnPropertyChanged(nameof(CalibrationTrialTitle));
        }

        CalibrationText = frame.Text;
        CalibrationMarkerColor = frame.MarkerColor;
        CalibrationX = frame.X;
        CalibrationY = frame.Y;
        CalibrationMoveDurationMilliseconds = (int)Math.Round(frame.MoveDuration.TotalMilliseconds);
        IsCalibrationMarkerVisible = true;
        CalibrationAnimationSequence++;
        activeCalibrationFrame = frame;
        activeCalibrationFrameStartedAt = now;
        RecordCalibrationFrameEvent(frame, now);
        calibrationTimer.Interval = frame.Duration;
        calibrationTimer.Start();
    }

    private void RecordCalibrationFrameEvent(CalibrationFrame frame, DateTimeOffset startedAt)
    {
        if (frame.Kind == CalibrationFrameKind.Point)
        {
            RecordModuleEventSafely(
                "eye_calibration_point_started",
                $"眼动校准第 {frame.TrialIndex} 轮第 {frame.PointIndex} 点开始",
                new
                {
                    trialIndex = frame.TrialIndex,
                    pointIndex = frame.PointIndex,
                    displayNumber = frame.Text,
                    positionType = frame.Region.HasValue ? (frame.Region == 1 ? "upper" : "lower") : "grid",
                    region = frame.Region,
                    xRatio = Math.Round(frame.X / 100d, 4),
                    yRatio = Math.Round(frame.Y / 100d, 4),
                    durationMs = (int)frame.Duration.TotalMilliseconds,
                    moveDurationMs = (int)frame.MoveDuration.TotalMilliseconds,
                    startedAtUnixMs = startedAt.ToUnixTimeMilliseconds()
                },
                startedAt,
                null);
            return;
        }

        RecordModuleEventSafely(
            frame.Kind == CalibrationFrameKind.StartCross
                ? "eye_calibration_trial_started"
                : "eye_calibration_trial_ending",
            frame.Kind == CalibrationFrameKind.StartCross
                ? $"眼动校准第 {frame.TrialIndex} 轮开始"
                : $"眼动校准第 {frame.TrialIndex} 轮结束十字开始",
            new
            {
                trialIndex = frame.TrialIndex,
                durationMs = (int)frame.Duration.TotalMilliseconds,
                startedAtUnixMs = startedAt.ToUnixTimeMilliseconds()
            },
            startedAt,
            null);
    }

    private void CompleteCalibrationPoint(DateTimeOffset endedAt)
    {
        if (activeCalibrationFrame is not { Kind: CalibrationFrameKind.Point } frame
            || activeCalibrationFrameStartedAt is not { } startedAt)
        {
            activeCalibrationFrame = null;
            activeCalibrationFrameStartedAt = null;
            return;
        }

        RecordModuleEventSafely(
            "eye_calibration_point_ended",
            $"眼动校准第 {frame.TrialIndex} 轮第 {frame.PointIndex} 点结束",
            new
            {
                trialIndex = frame.TrialIndex,
                pointIndex = frame.PointIndex,
                displayNumber = frame.Text,
                positionType = frame.Region.HasValue ? (frame.Region == 1 ? "upper" : "lower") : "grid",
                region = frame.Region,
                xRatio = Math.Round(frame.X / 100d, 4),
                yRatio = Math.Round(frame.Y / 100d, 4),
                pointShownAtUnixMs = startedAt.ToUnixTimeMilliseconds(),
                pointHiddenAtUnixMs = endedAt.ToUnixTimeMilliseconds(),
                durationMs = Math.Max(0, (long)(endedAt - startedAt).TotalMilliseconds)
            },
            startedAt,
            endedAt);

        activeCalibrationFrame = null;
        activeCalibrationFrameStartedAt = null;
    }

    private void ResetCalibrationSequence()
    {
        calibrationTimer.Stop();
        calibrationFrames.Clear();
        activeCalibrationFrame = null;
        activeCalibrationFrameStartedAt = null;
        calibrationTrialIndex = 1;
        CalibrationText = "+";
        CalibrationMarkerColor = EyeCalibrationSequenceFactory.CrossColor;
        CalibrationX = 50;
        CalibrationY = 50;
        CalibrationMoveDurationMilliseconds = 0;
        IsCalibrationMarkerVisible = false;
        CalibrationAnimationSequence++;
        OnPropertyChanged(nameof(CalibrationTrialTitle));
    }

    /// <summary>
    /// 视频浏览素材类型映射。
    /// 该类型仅作为后台元数据保留，界面不显示素材文件名和类型值。
    /// </summary>
    private static readonly IReadOnlyDictionary<string, int> VideoBrowseTypeByFileName =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["zz31.mp4"] = 3,
            ["fx05.mp4"] = 2,
            ["fx31.mp4"] = 2,
            ["zx29.mp4"] = 1
        };

    private VideoBrowseItem[] VideoBrowseItems => VideoBrowseVideoPaths
        .Select(CreateVideoBrowseItem)
        .ToArray();

    private static VideoBrowseItem CreateVideoBrowseItem(string videoPath)
    {
        var fileName = Path.GetFileName(videoPath);
        return new VideoBrowseItem(videoPath, GetVideoBrowseType(fileName));
    }

    private static string PictureBrowseDirectory => ResolveAssetPath("Assets", "CaptureWorkbench", "PictureBrowse");

    private static string PictureBrowseManifestPath => ResolveAssetPath(
        "Assets",
        "CaptureWorkbench",
        "PictureBrowse",
        PictureBrowseSequenceCatalog.ManifestFileName);

    private string[] VideoBrowseVideoPaths => Directory.Exists(VideoBrowseDirectory)
        ? Directory.GetFiles(VideoBrowseDirectory, "*.mp4").OrderBy(static path => path, StringComparer.OrdinalIgnoreCase).ToArray()
        : [];

    private static string VideoBrowseDirectory => ResolveAssetPath("Assets", "CaptureWorkbench", "VideoBrowse");

    private static int GetVideoBrowseType(string fileName)
    {
        return VideoBrowseTypeByFileName.TryGetValue(fileName, out var videoType)
            ? videoType
            : 0;
    }

    private static bool IsFormModuleCode(string moduleCode)
    {
        return string.Equals(moduleCode, BasicInfoModuleCode, StringComparison.Ordinal)
            || moduleCode.StartsWith("questionnaire_", StringComparison.Ordinal);
    }

    private static string PictureBrowseValenceCode(int valenceType)
    {
        return valenceType switch
        {
            1 => "P",
            2 => "U",
            3 => "N",
            _ => string.Empty
        };
    }

    /// <summary>
    /// 初始化图片浏览序列。版本及顺序均来自发布包清单，运行时不再打乱。
    /// </summary>
    private void BeginPictureBrowseSequence()
    {
        calibrationTimer.Stop();
        pictureBrowseTimer.Stop();
        pictureBrowseIndex = 0;
        pictureBrowseRestRemainingSeconds = 0;
        pictureBrowseRestPaused = false;
        pictureBrowseFixationStartedAt = null;
        pictureBrowseImageStartedAt = null;
        pictureBrowseRestStartedAt = null;
        pictureBrowseFinalBlankStartedAt = null;
        PictureBrowseImagePath = string.Empty;
        PictureBrowseRestText = string.Empty;

        var run = activeRun;
        pictureBrowseVersion = run is { } active
            ? PictureBrowseSequenceCatalog.ResolveStableVersion(active.RunId, active.PatientCode)
            : "A";
        var catalog = PictureBrowseSequenceCatalog.Load(PictureBrowseManifestPath, PictureBrowseDirectory);
        pictureBrowseItems = catalog.Get(pictureBrowseVersion).ToArray();

        pictureBrowsePhase = PictureBrowsePhase.Fixation;
        PictureBrowseStatusText = $"准备展示第 1 / {pictureBrowseItems.Length} 张";
        pictureBrowseFixationStartedAt = timeProvider.GetUtcNow();
        RecordModuleEventSafely(
            "picture_browse_sequence_started",
            $"图片浏览 {pictureBrowseVersion} 套序列开始",
            new
            {
                version = pictureBrowseVersion,
                total = pictureBrowseItems.Length,
                fixationDurationMs = PictureBrowseFixationMilliseconds,
                imageDurationMs = PictureBrowseImageMilliseconds,
                restAfterPosition = 15,
                restDurationSeconds = PictureBrowseRestSeconds,
                finalBlankDurationMs = PictureBrowseFinalBlankMilliseconds,
                startedAtUnixMs = UnixMilliseconds(pictureBrowseFixationStartedAt)
            },
            pictureBrowseFixationStartedAt,
            null);
        pictureBrowseTimer.Interval = TimeSpan.FromMilliseconds(PictureBrowseFixationMilliseconds);
        pictureBrowseTimer.Start();
        NotifyStageChanged();
    }

    /// <summary>
    /// 推进图片浏览内部状态：注视点 1 秒、图片 6 秒，第 15 张后休息 10 秒，
    /// 最后一张结束后灰色空白屏 500ms，再停止录制。
    /// </summary>
    private void AdvancePictureBrowse()
    {
        pictureBrowseTimer.Stop();

        if (!IsPictureBrowseModule || currentStep != CaptureWorkbenchStep.ModuleExecution)
        {
            ResetPictureBrowseState();
            return;
        }

        var items = pictureBrowseItems;
        if (items.Length == 0)
        {
            throw new InvalidOperationException("图片浏览序列为空。" );
        }

        var now = timeProvider.GetUtcNow();

        if (pictureBrowsePhase == PictureBrowsePhase.Resting)
        {
            if (pictureBrowseRestPaused)
            {
                return;
            }

            if (pictureBrowseRestRemainingSeconds > 1)
            {
                pictureBrowseRestRemainingSeconds--;
                UpdatePictureBrowseRestText(items.Length);
                pictureBrowseTimer.Interval = TimeSpan.FromSeconds(1);
                pictureBrowseTimer.Start();
                NotifyStageChanged();
                return;
            }

            pictureBrowseRestRemainingSeconds = 0;
            RecordModuleEventSafely(
                "picture_browse_rest_ended",
                "图片浏览第 15 张后休息结束",
                new
                {
                    completedPosition = 15,
                    endedAtUnixMs = now.ToUnixTimeMilliseconds()
                },
                pictureBrowseRestStartedAt,
                now);
            pictureBrowseRestStartedAt = null;
            PictureBrowseRestText = string.Empty;
            BeginPictureBrowseFixation(now);
            return;
        }

        if (pictureBrowsePhase == PictureBrowsePhase.FinalBlank)
        {
            RecordModuleEventSafely(
                "picture_browse_final_blank_ended",
                "图片浏览最后灰色空白屏结束",
                new
                {
                    durationMs = PictureBrowseFinalBlankMilliseconds,
                    endedAtUnixMs = now.ToUnixTimeMilliseconds()
                },
                pictureBrowseFinalBlankStartedAt,
                now);
            CompletePictureBrowse(now);
            return;
        }

        if (pictureBrowsePhase == PictureBrowsePhase.Fixation)
        {
            var fixationStartedAt = pictureBrowseFixationStartedAt ?? now;
            pictureBrowseFixationStartedAt = null;
            RecordModuleEventSafely(
                "picture_browse_fixation_ended",
                $"图片浏览第 {pictureBrowseIndex + 1} 张注视点结束",
                new
                {
                    position = pictureBrowseIndex + 1,
                    fixationStartedAtUnixMs = fixationStartedAt.ToUnixTimeMilliseconds(),
                    fixationEndedAtUnixMs = now.ToUnixTimeMilliseconds(),
                    durationMs = Math.Max(0, (long)(now - fixationStartedAt).TotalMilliseconds)
                },
                fixationStartedAt,
                now);

            var item = items[pictureBrowseIndex];
            pictureBrowsePhase = PictureBrowsePhase.ShowingImage;
            pictureBrowseImageStartedAt = now;
            PictureBrowseImagePath = item.ImagePath;
            CurrentPictureBrowseImageType = item.ValenceType;
            PictureBrowseStatusText = $"图片 {pictureBrowseIndex + 1} / {items.Length}";
            RecordModuleEventSafely(
                "picture_browse_image_started",
                $"图片浏览第 {item.Position} 张图片出现",
                new
                {
                    version = item.Version,
                    position = item.Position,
                    block6 = item.Block6,
                    fileName = item.FileName,
                    valence = item.Valence,
                    valenceCode = PictureBrowseValenceCode(item.ValenceType),
                    valenceType = item.ValenceType,
                    imageShownAtUnixMs = now.ToUnixTimeMilliseconds()
                },
                now,
                null);
            pictureBrowseTimer.Interval = TimeSpan.FromMilliseconds(PictureBrowseImageMilliseconds);
            pictureBrowseTimer.Start();
            NotifyStageChanged();
            return;
        }

        if (pictureBrowsePhase == PictureBrowsePhase.ShowingImage)
        {
            var item = items[pictureBrowseIndex];
            var imageStartedAt = pictureBrowseImageStartedAt ?? now;
            RecordModuleEventSafely(
                "picture_browse_image_ended",
                $"图片浏览第 {item.Position} 张图片消失",
                new
                {
                    version = item.Version,
                    position = item.Position,
                    block6 = item.Block6,
                    fileName = item.FileName,
                    valence = item.Valence,
                    valenceCode = PictureBrowseValenceCode(item.ValenceType),
                    valenceType = item.ValenceType,
                    imageShownAtUnixMs = imageStartedAt.ToUnixTimeMilliseconds(),
                    imageHiddenAtUnixMs = now.ToUnixTimeMilliseconds(),
                    durationMs = Math.Max(0, (long)(now - imageStartedAt).TotalMilliseconds)
                },
                imageStartedAt,
                now);
            pictureBrowseImageStartedAt = null;
            pictureBrowseIndex++;
            PictureBrowseImagePath = string.Empty;
            CurrentPictureBrowseImageType = null;

            if (pictureBrowseIndex >= items.Length)
            {
                pictureBrowsePhase = PictureBrowsePhase.FinalBlank;
                pictureBrowseFinalBlankStartedAt = now;
                PictureBrowseStatusText = string.Empty;
                RecordModuleEventSafely(
                    "picture_browse_final_blank_started",
                    "图片浏览进入最后灰色空白屏",
                    new
                    {
                        durationMs = PictureBrowseFinalBlankMilliseconds,
                        startedAtUnixMs = now.ToUnixTimeMilliseconds()
                    },
                    now,
                    null);
                pictureBrowseTimer.Interval = TimeSpan.FromMilliseconds(PictureBrowseFinalBlankMilliseconds);
                pictureBrowseTimer.Start();
                NotifyStageChanged();
                return;
            }

            if (pictureBrowseIndex == 15)
            {
                pictureBrowsePhase = PictureBrowsePhase.Resting;
                pictureBrowseRestRemainingSeconds = PictureBrowseRestSeconds;
                pictureBrowseRestStartedAt = now;
                UpdatePictureBrowseRestText(items.Length);
                PictureBrowseStatusText = $"强制休息中：已完成 {pictureBrowseIndex} / {items.Length} 张";
                RecordModuleEventSafely(
                    "picture_browse_rest_started",
                    "图片浏览第 15 张后进入 10 秒休息",
                    new
                    {
                        completedPosition = pictureBrowseIndex,
                        durationSeconds = PictureBrowseRestSeconds,
                        startedAtUnixMs = now.ToUnixTimeMilliseconds()
                    },
                    now,
                    null);
                pictureBrowseTimer.Interval = TimeSpan.FromSeconds(1);
                pictureBrowseTimer.Start();
                NotifyStageChanged();
                return;
            }

            BeginPictureBrowseFixation(now);
            return;
        }

        if (pictureBrowsePhase == PictureBrowsePhase.Idle)
        {
            BeginPictureBrowseFixation(now);
        }
    }

    private void BeginPictureBrowseFixation(DateTimeOffset startedAt)
    {
        if (pictureBrowseIndex >= pictureBrowseItems.Length)
        {
            return;
        }

        pictureBrowsePhase = PictureBrowsePhase.Fixation;
        pictureBrowseFixationStartedAt = startedAt;
        PictureBrowseImagePath = string.Empty;
        CurrentPictureBrowseImageType = null;
        PictureBrowseStatusText = $"准备展示第 {pictureBrowseIndex + 1} / {pictureBrowseItems.Length} 张";
        pictureBrowseTimer.Interval = TimeSpan.FromMilliseconds(PictureBrowseFixationMilliseconds);
        pictureBrowseTimer.Start();
        NotifyStageChanged();
    }

    /// <summary>
    /// 图片浏览全部素材展示完成，进入模块完成阶段。
    /// </summary>
    private void CompletePictureBrowse(DateTimeOffset completedAt)
    {
        pictureBrowseTimer.Stop();
        pictureBrowsePhase = PictureBrowsePhase.Completed;
        pictureBrowseRestRemainingSeconds = 0;
        PictureBrowseImagePath = string.Empty;
        CurrentPictureBrowseImageType = null;
        PictureBrowseStatusText = "图片浏览完成";
        PictureBrowseRestText = string.Empty;
        RecordModuleEventSafely(
            "picture_browse_sequence_completed",
            $"图片浏览 {pictureBrowseVersion} 套序列完成",
            new
            {
                version = pictureBrowseVersion,
                total = pictureBrowseItems.Length,
                completedAtUnixMs = completedAt.ToUnixTimeMilliseconds()
            },
            completedAt,
            completedAt);
        MoveToStep(CaptureWorkbenchStep.Completed);
        NotifyStageChanged();
    }

    private void UpdatePictureBrowseRestText(int totalCount)
    {
        var pauseText = pictureBrowseRestPaused ? "\n检测到人脸位置变化，请调整后继续。" : string.Empty;
        PictureBrowseRestText = $"已完成 {pictureBrowseIndex} / {totalCount} 张图片\n剩余 {pictureBrowseRestRemainingSeconds} 秒后自动继续。{pauseText}";
    }

    internal void ObservePictureBrowseRestFace(
        CameraFaceState state,
        bool isPrimaryFaceInsideGuide,
        DateTimeOffset observedAt)
    {
        if (!IsPictureResting || pictureBrowseRestRemainingSeconds > 5)
        {
            return;
        }

        var isReady = state == CameraFaceState.Normal && isPrimaryFaceInsideGuide;
        if (!isReady && !pictureBrowseRestPaused)
        {
            pictureBrowseRestPaused = true;
            pictureBrowseTimer.Stop();
            UpdatePictureBrowseRestText(pictureBrowseItems.Length);
            RecordModuleEventSafely(
                "picture_browse_rest_face_check_failed",
                "图片浏览休息末段人脸取景未通过，暂停倒计时",
                new
                {
                    remainingSeconds = pictureBrowseRestRemainingSeconds,
                    failedAtUnixMs = observedAt.ToUnixTimeMilliseconds()
                },
                observedAt,
                null);
            NotifyStageChanged();
            return;
        }

        if (isReady && pictureBrowseRestPaused)
        {
            pictureBrowseRestPaused = false;
            UpdatePictureBrowseRestText(pictureBrowseItems.Length);
            var resumedAt = observedAt;
            RecordModuleEventSafely(
                "picture_browse_rest_face_check_recovered",
                "图片浏览休息末段人脸取景恢复，继续倒计时",
                new
                {
                    remainingSeconds = pictureBrowseRestRemainingSeconds,
                    recoveredAtUnixMs = resumedAt.ToUnixTimeMilliseconds()
                },
                resumedAt,
                null);
            pictureBrowseTimer.Interval = TimeSpan.FromSeconds(1);
            pictureBrowseTimer.Start();
            NotifyStageChanged();
        }
    }

    /// <summary>
    /// 初始化视频浏览序列。
    /// 四个正式视频按需求随机播放，文件名与类型只作为后台元数据，不显示在界面上。
    /// </summary>
    private void BeginVideoBrowseSequence()
    {
        calibrationTimer.Stop();
        pictureBrowseTimer.Stop();
        videoBrowseTimer.Stop();
        videoBrowseIndex = 0;
        VideoBrowseVideoPath = string.Empty;
        VideoBrowseRestText = string.Empty;
        CurrentVideoBrowseVideoType = null;

        videoBrowseItems = VideoBrowseItems
            .OrderBy(_ => videoBrowseRandom.Next())
            .ToArray();

        if (videoBrowseItems.Length == 0)
        {
            videoBrowsePhase = VideoBrowsePhase.Idle;
            VideoBrowseStatusText = "未找到视频浏览素材";
            StageNoticeText = "未找到视频浏览素材，请检查素材库 Assets/CaptureWorkbench/VideoBrowse。";
            MoveToStep(CaptureWorkbenchStep.FaceCheck);
            return;
        }

        videoBrowsePhase = VideoBrowsePhase.Blank;
        VideoBrowseStatusText = $"准备播放第 1 / {videoBrowseItems.Length} 段视频";
        videoBrowseTimer.Interval = TimeSpan.FromMilliseconds(VideoBrowseBlankMilliseconds);
        videoBrowseTimer.Start();
    }

    /// <summary>
    /// 推进视频浏览内部状态。
    /// 规则：休息 12 秒不可跳过，休息结束后进入 2 秒空屏，再播放下一段视频。
    /// 因此相邻两段真实视频之间的固定间隔为 14 秒，可用于后续音视频采集时间轴推断。
    /// 视频结束事件由 View 层的 MediaElement 回调 CompleteCurrentVideoBrowseVideo。
    /// </summary>
    private void AdvanceVideoBrowseAfterBlank()
    {
        videoBrowseTimer.Stop();

        if (!IsVideoBrowseModule || currentStep != CaptureWorkbenchStep.ModuleExecution)
        {
            ResetVideoBrowseState();
            return;
        }

        if (videoBrowseItems.Length == 0)
        {
            VideoBrowseStatusText = "未找到视频浏览素材";
            NotifyStageChanged();
            return;
        }

        if (videoBrowsePhase == VideoBrowsePhase.Resting)
        {
            if (videoBrowseRestRemainingSeconds > 1)
            {
                videoBrowseRestRemainingSeconds--;
                UpdateVideoBrowseRestText();
                videoBrowseTimer.Interval = TimeSpan.FromSeconds(1);
                videoBrowseTimer.Start();
                NotifyStageChanged();
                return;
            }

            videoBrowsePhase = VideoBrowsePhase.Blank;
            videoBrowseRestRemainingSeconds = 0;
            VideoBrowseVideoPath = string.Empty;
            VideoBrowseRestText = string.Empty;
            VideoBrowseStatusText = $"休息结束，准备播放第 {videoBrowseIndex + 1} / {videoBrowseItems.Length} 段视频";
            videoBrowseTimer.Interval = TimeSpan.FromMilliseconds(VideoBrowseBlankMilliseconds);
            videoBrowseTimer.Start();
            NotifyStageChanged();
            return;
        }

        if (videoBrowseIndex >= videoBrowseItems.Length)
        {
            CompleteVideoBrowse();
            return;
        }

        var item = videoBrowseItems[videoBrowseIndex];
        videoBrowsePhase = VideoBrowsePhase.PlayingVideo;
        VideoBrowseVideoPath = item.VideoPath;
        CurrentVideoBrowseVideoType = item.VideoType;
        VideoBrowseRestText = string.Empty;
        VideoBrowseStatusText = $"视频 {videoBrowseIndex + 1} / {videoBrowseItems.Length}";
        currentVideoBrowseStartedAt = DateTimeOffset.Now;
        RecordModuleEventSafely(
            "video_browse_video_started",
            $"视频浏览第 {videoBrowseIndex + 1} 段开始播放",
            new
            {
                index = videoBrowseIndex + 1,
                total = videoBrowseItems.Length,
                videoType = item.VideoType,
                fileName = Path.GetFileName(item.VideoPath),
                startedAtUnixMs = currentVideoBrowseStartedAt.Value.ToUnixTimeMilliseconds()
            },
            currentVideoBrowseStartedAt,
            null);
        NotifyStageChanged();
    }

    private void UpdateVideoBrowseRestText()
    {
        VideoBrowseRestText = $"已完成 {videoBrowseIndex} / {videoBrowseItems.Length} 段视频\n剩余 {videoBrowseRestRemainingSeconds} 秒后自动继续。";
    }

    /// <summary>
    /// 视频浏览全部素材播放完成，进入模块完成阶段。
    /// </summary>
    private void CompleteVideoBrowse()
    {
        videoBrowseTimer.Stop();
        videoBrowsePhase = VideoBrowsePhase.Completed;
        videoBrowseRestRemainingSeconds = 0;
        VideoBrowseVideoPath = string.Empty;
        CurrentVideoBrowseVideoType = null;
        currentVideoBrowseStartedAt = null;
        VideoBrowseStatusText = "视频浏览完成";
        VideoBrowseRestText = string.Empty;
        MoveToStep(CaptureWorkbenchStep.Completed);
        NotifyStageChanged();
    }

    /// <summary>
    /// 推进开发专用音画同步测试倒计时。
    /// 倒计时结束后进入模块完成阶段，下一帧到来时由 View 触发录制服务正常收尾。
    /// </summary>
    private void AdvanceSyncTest()
    {
        if (!IsSyncTestModule || currentStep != CaptureWorkbenchStep.ModuleExecution || !isSyncTestRunning)
        {
            ResetSyncTestState();
            NotifyStageChanged();
            return;
        }

        if (syncTestRemainingSeconds > 1)
        {
            syncTestRemainingSeconds--;
            NotifyStageChanged();
            return;
        }

        syncTestTimer.Stop();
        syncTestRemainingSeconds = 0;
        isSyncTestRunning = false;
        StageNoticeText = "音画同步测试录制完成，正在合成音视频。";
        MoveToStep(CaptureWorkbenchStep.Completed);
        NotifyStageChanged();
    }

    /// <summary>
    /// 初始化语音基线序列。
    /// 模块级音视频录制已经开始，此处只控制三段提示词的时间戳和 UI 状态。
    /// </summary>
}
