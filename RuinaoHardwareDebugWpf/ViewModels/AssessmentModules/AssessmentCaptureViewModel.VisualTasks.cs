namespace RuinaoHardwareDebugWpf;

using System.IO;

public sealed partial class AssessmentCaptureViewModel
{
    private void BeginCalibrationSequence()
    {
        pictureBrowseTimer.Stop();
        calibrationTimer.Stop();
        calibrationFrames.Clear();

        for (var trialIndex = 0; trialIndex < calibrationTrials.Length; trialIndex++)
        {
            var trial = calibrationTrials[trialIndex];
            calibrationFrames.Enqueue(new("+", 50, 50, TimeSpan.FromMilliseconds(trial.FirstCrossMs)));

            for (var pointNumber = 1; pointNumber <= trial.PointCount; pointNumber++)
            {
                var (x, y) = trial.IsFixedLayout
                    ? PositionForFixedPoint(pointNumber, trial.LayoutValues)
                    : PositionForRegionPoint(pointNumber, trial.LayoutValues[pointNumber - 1]);
                calibrationFrames.Enqueue(new(pointNumber.ToString(), x, y, TimeSpan.FromMilliseconds(trial.NumberMs)));
            }

            calibrationFrames.Enqueue(new("+", 50, 50, TimeSpan.FromMilliseconds(trial.LastCrossMs)));
        }

        CalibrationStatusText = "校准进行中";
        ShowNextCalibrationFrame();
    }

    /// <summary>
    /// 推进眼动校准点序列。
    /// 队列为空时表示本模块第三步完成，进入模块完成阶段。
    /// </summary>
    private void ShowNextCalibrationFrame()
    {
        if (calibrationFrames.Count == 0)
        {
            calibrationTimer.Stop();
            CalibrationText = "完成";
            CalibrationX = 50;
            CalibrationY = 50;
            CalibrationStatusText = "校准完成，准备进入图片浏览";
            MoveToStep(CaptureWorkbenchStep.Completed);
            NotifyStageChanged();
            return;
        }

        var frame = calibrationFrames.Dequeue();
        CalibrationText = frame.Text;
        CalibrationX = frame.X;
        CalibrationY = frame.Y;
        OnPropertyChanged(nameof(CalibrationCanvasLeft));
        OnPropertyChanged(nameof(CalibrationCanvasTop));
        calibrationTimer.Interval = frame.Duration;
        calibrationTimer.Start();
    }

    /// <summary>
    /// 图片浏览当前固定素材顺序对应的图片类型。
    /// 业务含义暂未确定，先按需求记录 30 张图片的 1/2/3 类型，后续可用于数据库事件或结果分析。
    /// </summary>
    private static readonly int[] PictureBrowseImageTypeSequence =
    [
        1, 2, 3, 3, 2,
        1, 2, 3, 1, 3,
        1, 2, 1, 2, 3,
        2, 1, 3, 3, 2,
        1, 3, 1, 2, 1,
        2, 3, 3, 2, 1
    ];

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

    private PictureBrowseItem[] PictureBrowseItems => PictureBrowseImagePaths
        .Select((path, index) => new PictureBrowseItem(path, GetPictureBrowseImageType(index)))
        .ToArray();

    private VideoBrowseItem[] VideoBrowseItems => VideoBrowseVideoPaths
        .Select(CreateVideoBrowseItem)
        .ToArray();

    private static VideoBrowseItem CreateVideoBrowseItem(string videoPath)
    {
        var fileName = Path.GetFileName(videoPath);
        return new VideoBrowseItem(videoPath, GetVideoBrowseType(fileName));
    }

    private string[] PictureBrowseImagePaths => Directory.Exists(PictureBrowseDirectory)
        ? Directory.GetFiles(PictureBrowseDirectory, "*.png").OrderBy(static path => path, StringComparer.OrdinalIgnoreCase).ToArray()
        : [];

    private static string PictureBrowseDirectory => ResolveAssetPath("Assets", "CaptureWorkbench", "PictureBrowse");

    private string[] VideoBrowseVideoPaths => Directory.Exists(VideoBrowseDirectory)
        ? Directory.GetFiles(VideoBrowseDirectory, "*.mp4").OrderBy(static path => path, StringComparer.OrdinalIgnoreCase).ToArray()
        : [];

    private static string VideoBrowseDirectory => ResolveAssetPath("Assets", "CaptureWorkbench", "VideoBrowse");

    private static int GetPictureBrowseImageType(int zeroBasedIndex)
    {
        return zeroBasedIndex >= 0 && zeroBasedIndex < PictureBrowseImageTypeSequence.Length
            ? PictureBrowseImageTypeSequence[zeroBasedIndex]
            : 0;
    }

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

    /// <summary>
    /// 初始化图片浏览序列。
    /// 图片按素材库文件名前缀排序，当前前缀来自创建时间排序后的复制结果。
    /// </summary>
    private void BeginPictureBrowseSequence()
    {
        calibrationTimer.Stop();
        pictureBrowseTimer.Stop();
        pictureBrowseIndex = 0;
        PictureBrowseImagePath = string.Empty;
        PictureBrowseRestText = string.Empty;

        if (PictureBrowseItems.Length == 0)
        {
            pictureBrowsePhase = PictureBrowsePhase.Idle;
            PictureBrowseStatusText = "未找到图片浏览素材";
            StageNoticeText = "未找到图片浏览素材，请检查素材库 Assets/CaptureWorkbench/PictureBrowse。";
            MoveToStep(CaptureWorkbenchStep.FaceCheck);
            return;
        }

        pictureBrowsePhase = PictureBrowsePhase.Blank;
        PictureBrowseStatusText = $"准备展示第 1 / {PictureBrowseItems.Length} 张";
        pictureBrowseTimer.Interval = TimeSpan.FromMilliseconds(300);
        pictureBrowseTimer.Start();
    }

    /// <summary>
    /// 推进图片浏览内部状态。
    /// 规则：图片 6 秒、空屏 2.5 秒，第 12 和 24 张后强制休息 12 秒并自动继续。
    /// </summary>
    private void AdvancePictureBrowse()
    {
        pictureBrowseTimer.Stop();

        if (!IsPictureBrowseModule || currentStep != CaptureWorkbenchStep.ModuleExecution)
        {
            ResetPictureBrowseState();
            return;
        }

        var items = PictureBrowseItems;
        if (items.Length == 0)
        {
            PictureBrowseStatusText = "未找到图片浏览素材";
            NotifyStageChanged();
            return;
        }

        if (pictureBrowsePhase == PictureBrowsePhase.Resting)
        {
            if (pictureBrowseRestRemainingSeconds > 1)
            {
                pictureBrowseRestRemainingSeconds--;
                UpdatePictureBrowseRestText(items.Length);
                pictureBrowseTimer.Interval = TimeSpan.FromSeconds(1);
                pictureBrowseTimer.Start();
                NotifyStageChanged();
                return;
            }

            pictureBrowsePhase = PictureBrowsePhase.Blank;
            pictureBrowseRestRemainingSeconds = 0;
            PictureBrowseRestText = string.Empty;
            PictureBrowseStatusText = $"休息结束，准备继续展示第 {pictureBrowseIndex + 1} / {items.Length} 张";
            pictureBrowseTimer.Interval = TimeSpan.FromMilliseconds(300);
            pictureBrowseTimer.Start();
            NotifyStageChanged();
            return;
        }

        if (pictureBrowsePhase is PictureBrowsePhase.Idle or PictureBrowsePhase.Blank)
        {
            if (pictureBrowseIndex >= items.Length)
            {
                CompletePictureBrowse();
                return;
            }

            var item = items[pictureBrowseIndex];
            pictureBrowsePhase = PictureBrowsePhase.ShowingImage;
            PictureBrowseImagePath = item.ImagePath;
            CurrentPictureBrowseImageType = item.ImageType;
            PictureBrowseStatusText = $"图片 {pictureBrowseIndex + 1} / {items.Length}";
            pictureBrowseTimer.Interval = TimeSpan.FromMilliseconds(6000);
            pictureBrowseTimer.Start();
            NotifyStageChanged();
            return;
        }

        if (pictureBrowsePhase == PictureBrowsePhase.ShowingImage)
        {
            pictureBrowseIndex++;
            PictureBrowseImagePath = string.Empty;
            CurrentPictureBrowseImageType = null;

            if (pictureBrowseIndex >= items.Length)
            {
                CompletePictureBrowse();
                return;
            }

            if (pictureBrowseIndex % 12 == 0)
            {
                pictureBrowsePhase = PictureBrowsePhase.Resting;
                pictureBrowseRestRemainingSeconds = CaptureWorkbenchForcedRestSeconds;
                UpdatePictureBrowseRestText(items.Length);
                PictureBrowseStatusText = $"强制休息中：已完成 {pictureBrowseIndex} / {items.Length} 张";
                pictureBrowseTimer.Interval = TimeSpan.FromSeconds(1);
                pictureBrowseTimer.Start();
                NotifyStageChanged();
                return;
            }

            pictureBrowsePhase = PictureBrowsePhase.Blank;
            PictureBrowseStatusText = string.Empty;
            pictureBrowseTimer.Interval = TimeSpan.FromMilliseconds(2500);
            pictureBrowseTimer.Start();
            NotifyStageChanged();
        }
    }

    /// <summary>
    /// 图片浏览全部素材展示完成，进入模块完成阶段。
    /// </summary>
    private void CompletePictureBrowse()
    {
        pictureBrowseTimer.Stop();
        pictureBrowsePhase = PictureBrowsePhase.Completed;
        pictureBrowseRestRemainingSeconds = 0;
        PictureBrowseImagePath = string.Empty;
        CurrentPictureBrowseImageType = null;
        PictureBrowseStatusText = "图片浏览完成";
        PictureBrowseRestText = string.Empty;
        MoveToStep(CaptureWorkbenchStep.Completed);
        NotifyStageChanged();
    }

    private void UpdatePictureBrowseRestText(int totalCount)
    {
        PictureBrowseRestText = $"已完成 {pictureBrowseIndex} / {totalCount} 张图片\n剩余 {pictureBrowseRestRemainingSeconds} 秒后自动继续。";
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
