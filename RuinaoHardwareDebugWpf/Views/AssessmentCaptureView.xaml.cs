using System.Windows.Controls;
using System.Globalization;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Windows.Threading;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using OpenCvSharp;

namespace RuinaoHardwareDebugWpf.Views;

/// <summary>
/// 采集工作台页面。
/// View 层负责按钮事件、演示视频播放、摄像头预览和人脸框绘制；
/// 模块流程状态在 ViewModel 中维护，音视频录制由 ICaptureMediaRecorder 服务处理。
/// </summary>
public partial class AssessmentCaptureView : UserControl
{
    private readonly DispatcherTimer playbackTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(300)
    };

    private readonly DispatcherTimer cameraTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(80)
    };

    private readonly ICameraCaptureService cameraCaptureService = AppComposition.Services.GetRequiredService<ICameraCaptureService>();
    private Mat? cameraFrame;
    private bool cameraPreviewHasFrame;
    private bool faceInGuideFrame;
    private DateTime lastFaceOkAt = DateTime.MinValue;
    private DateOnly displayedBasicInfoCalendarMonth = FirstDayOfMonth(DateOnly.FromDateTime(DateTime.Today));

    public AssessmentCaptureView()
    {
        InitializeComponent();
        playbackTimer.Tick += (_, _) => UpdatePlaybackTime();
        cameraTimer.Tick += (_, _) => UpdateCameraPreview();
        Loaded += (_, _) => StartPageActivities();
        Unloaded += (_, _) => StopPageActivitiesForUnload();
    }

    private AssessmentCaptureViewModel? ViewModel => DataContext as AssessmentCaptureViewModel;

    private void StartPageActivities()
    {
        // 如果用户离开演示播放页后又返回，MediaElement 不会自动恢复画面。
        // 这里兜底清理“播放中但没有播放器上下文”的状态，让用户重新完整观看演示。
        ViewModel?.CancelDemoPlaybackForNavigation();
        StartCameraPreview();
    }

    private void BasicInfoBirthDateButton_Click(object sender, RoutedEventArgs e)
    {
        var currentText = ViewModel?.BasicInfoBirthDateText.Trim() ?? string.Empty;
        displayedBasicInfoCalendarMonth = DateOnly.TryParseExact(
            currentText,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var selectedDate)
            ? FirstDayOfMonth(selectedDate)
            : FirstDayOfMonth(DateOnly.FromDateTime(DateTime.Today));

        RenderBasicInfoCalendarDays();
        BasicInfoDatePickerPopup.IsOpen = true;
    }

    private void BasicInfoPreviousMonthButton_Click(object sender, RoutedEventArgs e)
    {
        displayedBasicInfoCalendarMonth = displayedBasicInfoCalendarMonth.AddMonths(-1);
        RenderBasicInfoCalendarDays();
    }

    private void BasicInfoNextMonthButton_Click(object sender, RoutedEventArgs e)
    {
        displayedBasicInfoCalendarMonth = displayedBasicInfoCalendarMonth.AddMonths(1);
        RenderBasicInfoCalendarDays();
    }

    private void BasicInfoCalendarDayButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: DateOnly selectedDate } || ViewModel is null)
        {
            return;
        }

        ViewModel.BasicInfoBirthDateText = selectedDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        BasicInfoDatePickerPopup.IsOpen = false;
    }

    private void RenderBasicInfoCalendarDays()
    {
        BasicInfoCalendarTitleText.Text = displayedBasicInfoCalendarMonth.ToString("yyyy 年 MM 月", CultureInfo.InvariantCulture);
        BasicInfoCalendarDaysGrid.Children.Clear();

        var firstDayOffset = ((int)displayedBasicInfoCalendarMonth.DayOfWeek + 6) % 7;
        var gridStartDate = displayedBasicInfoCalendarMonth.AddDays(-firstDayOffset);
        var selectedDate = TryReadBasicInfoSelectedDate();
        var today = DateOnly.FromDateTime(DateTime.Today);

        for (var index = 0; index < 42; index++)
        {
            var date = gridStartDate.AddDays(index);
            var dayButton = new Button
            {
                Content = date.Day.ToString(CultureInfo.InvariantCulture),
                Tag = date,
                Style = (Style)FindResource("CalendarDayButton"),
            };
            dayButton.Click += BasicInfoCalendarDayButton_Click;

            if (date.Month != displayedBasicInfoCalendarMonth.Month)
            {
                dayButton.Foreground = (Brush)FindResource("SubText");
                dayButton.Opacity = 0.38;
            }

            if (date == today)
            {
                dayButton.BorderBrush = (Brush)FindResource("Line");
                dayButton.Background = new SolidColorBrush(Color.FromRgb(45, 51, 66));
            }

            if (selectedDate == date)
            {
                dayButton.Foreground = (Brush)FindResource("Text");
                dayButton.BorderBrush = (Brush)FindResource("Gold");
                dayButton.Background = new SolidColorBrush(Color.FromRgb(58, 46, 29));
                dayButton.FontWeight = FontWeights.SemiBold;
                dayButton.Opacity = 1;
            }

            BasicInfoCalendarDaysGrid.Children.Add(dayButton);
        }
    }

    private DateOnly? TryReadBasicInfoSelectedDate()
    {
        var currentText = ViewModel?.BasicInfoBirthDateText.Trim() ?? string.Empty;
        return DateOnly.TryParseExact(currentText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var selectedDate)
            ? selectedDate
            : null;
    }

    private static DateOnly FirstDayOfMonth(DateOnly date)
    {
        return new DateOnly(date.Year, date.Month, 1);
    }

    private void StopPageActivitiesForUnload()
    {
        playbackTimer.Stop();
        DemoMedia.Stop();
        VideoBrowseMedia.Stop();
        ViewModel?.CancelDemoPlaybackForNavigation();
        StopCameraPreview();
    }

    private void PlayDemoButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var viewModel = ViewModel;
        if (viewModel is null)
        {
            return;
        }

        viewModel.BeginDemoPlayback();
        VideoBrowseMedia.Stop();
        DemoMedia.Stop();
        DemoMedia.Position = TimeSpan.Zero;
        DemoMedia.Play();
        playbackTimer.Start();
    }

    private async void StartCalibrationButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var viewModel = ViewModel;
        if (viewModel is null)
        {
            return;
        }

        playbackTimer.Stop();
        DemoMedia.Stop();
        VideoBrowseMedia.Stop();

        if (viewModel.IsDemoStep)
        {
            viewModel.BeginFaceCheck();
            StartCameraPreview();
            return;
        }

        if (!viewModel.HasSelectedCamera)
        {
            viewModel.ShowStageNotice(viewModel.Localize("CaptureWorkspaceNoCameraStageNotice"));
            return;
        }

        if (!cameraPreviewHasFrame)
        {
            StartCameraPreview();
            viewModel.ShowStageNotice(viewModel.Localize("CaptureWorkspaceCameraNoFrameStageNotice"));
            return;
        }

        if (!faceInGuideFrame && DateTime.Now - lastFaceOkAt > TimeSpan.FromSeconds(1.5))
        {
            viewModel.ShowStageNotice(viewModel.Localize("CaptureWorkspaceFaceNotReadyStageNotice"));
            return;
        }

        try
        {
            await BeginModuleRecordingSessionAsync(viewModel);
        }
        catch (Exception exception)
        {
            StopModuleRecording(viewModel, "failed", viewModel.Localize("CaptureWorkspaceMediaStartFailed", exception.Message));
            viewModel.ShowStageNotice(viewModel.Localize("CaptureWorkspaceMediaStartFailedNotice", exception.Message));
            return;
        }

        viewModel.StartCurrentModule();
    }

    private async void StartSyncTestButton_Click(object sender, RoutedEventArgs e)
    {
        var viewModel = ViewModel;
        if (viewModel is null)
        {
            return;
        }

        playbackTimer.Stop();
        DemoMedia.Stop();
        VideoBrowseMedia.Stop();

        if (!viewModel.HasSelectedCamera)
        {
            viewModel.ShowStageNotice(viewModel.Localize("CaptureWorkspaceNoCameraStageNotice"));
            return;
        }

        if (!cameraPreviewHasFrame)
        {
            StartCameraPreview();
            viewModel.ShowStageNotice(viewModel.Localize("CaptureWorkspaceCameraNoFrameStageNotice"));
            return;
        }

        try
        {
            await BeginModuleRecordingSessionAsync(viewModel);
        }
        catch (Exception exception)
        {
            StopModuleRecording(viewModel, "failed", viewModel.Localize("CaptureWorkspaceSyncTestStartFailed", exception.Message));
            viewModel.ShowStageNotice(viewModel.Localize("CaptureWorkspaceSyncTestStartFailedNotice", exception.Message));
            return;
        }

        viewModel.StartSyncTest();
    }

    private void StartVoiceBaselineButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel?.StartVoiceBaselineFirstSegment();
    }

    private void StartWordReadingButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel?.StartWordReadingFirstGroup();
    }

    private void RefreshCameraButton_Click(object sender, RoutedEventArgs e)
    {
        StartCameraPreview();
    }

    private void CameraComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded)
        {
            StartCameraPreview();
        }
    }

    private void DemoMedia_MediaEnded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (ViewModel is null || ViewModel.IsDemoCompleted)
        {
            return;
        }

        playbackTimer.Stop();
        DemoMedia.Stop();
        DemoMedia.Position = TimeSpan.Zero;
        ViewModel.CompleteDemo();
    }

    private void VideoBrowseMedia_MediaOpened(object sender, RoutedEventArgs e)
    {
        VideoBrowseMedia.Position = TimeSpan.Zero;
        VideoBrowseMedia.Play();
    }

    private void VideoBrowseMedia_MediaEnded(object sender, RoutedEventArgs e)
    {
        VideoBrowseMedia.Stop();
        ViewModel?.CompleteCurrentVideoBrowseVideo();
    }

    private void UpdatePlaybackTime()
    {
        var duration = DemoMedia.NaturalDuration.HasTimeSpan
            ? DemoMedia.NaturalDuration.TimeSpan
            : (TimeSpan?)null;

        ViewModel?.UpdatePlaybackTime(DemoMedia.Position, duration);
    }

    private void StartCameraPreview()
    {
        // 切换摄像头或重新进入页面时，先释放旧预览，避免设备被重复占用。
        StopCameraPreview();

        var viewModel = ViewModel;
        if (viewModel is null || !viewModel.HasSelectedCamera)
        {
            CameraPreviewImage.Source = null;
            CameraPreviewStatusText.Text = viewModel?.Localize("CaptureWorkspaceNoCameraSelected")
                ?? string.Empty;
            return;
        }

        var cameraIndex = CameraComboBox.SelectedIndex < 0 ? 0 : CameraComboBox.SelectedIndex;
        CameraPreviewStatusText.Text = viewModel.Localize("CaptureWorkspaceOpeningCamera");
        if (!cameraCaptureService.Open(cameraIndex))
        {
            StopCameraPreview();
            CameraPreviewStatusText.Text = viewModel.Localize("CaptureWorkspaceCameraOpenFailed");
            return;
        }

        cameraFrame = new Mat();
        cameraTimer.Start();
    }

    private void StopCameraPreview()
    {
        cameraTimer.Stop();
        StopRecordingForPreviewStop();
        cameraFrame?.Dispose();
        cameraFrame = null;
        cameraCaptureService.Close();
        cameraPreviewHasFrame = false;
    }

    private void StopRecordingForPreviewStop()
    {
        var viewModel = ViewModel;
        if (viewModel is null || !viewModel.CaptureMediaRecorder.IsRecording)
        {
            return;
        }

        if (viewModel.IsCompletionStage)
        {
            // 模块已经正常完成时，离开页面只触发正常收尾和合成。
            StopModuleRecording(viewModel, "completed", viewModel.Localize("CaptureWorkspaceModuleMediaCompleted", viewModel.CurrentModule));
            return;
        }

        // 第三步正式采集中切换页面或关闭程序，视为中断。
        // 中断数据不合成、不作为有效记录，录制服务会尝试删除临时音视频文件。
        var message = viewModel.Localize("CaptureWorkspaceRecordingInterruptedMessage");
        StopModuleRecording(viewModel, "discarded", message);
        viewModel.AbortCurrentModuleExecution(message);
    }

    private void UpdateCameraPreview()
    {
        if (!cameraCaptureService.IsOpen || cameraFrame is null)
        {
            return;
        }

        if (!cameraCaptureService.Read(cameraFrame))
        {
            CameraPreviewStatusText.Text = ViewModel?.Localize("CaptureWorkspaceNoFrameRead") ?? string.Empty;
            return;
        }

        RecordFrameIfNeeded(cameraFrame);
        var faceStatus = UpdateFaceDetectionOverlay(cameraFrame);

        using var bgra = new Mat();
        Cv2.CvtColor(cameraFrame, bgra, ColorConversionCodes.BGR2BGRA);
        var bitmap = BitmapSource.Create(
            bgra.Width,
            bgra.Height,
            96,
            96,
            System.Windows.Media.PixelFormats.Bgra32,
            null,
            bgra.Data,
            (int)(bgra.Step() * bgra.Height),
            (int)bgra.Step());

        bitmap.Freeze();
        CameraPreviewImage.Source = bitmap;
        cameraPreviewHasFrame = true;
        CameraPreviewStatusText.Text = faceStatus;
    }

    private string UpdateFaceDetectionOverlay(Mat frame)
    {
        // 当前阶段使用肤色区域近似人脸检测，主要用于流程原型和取景提示。
        // 后续如果接入正式人脸识别 SDK，可替换 DetectFaceLikeRegion。
        var guideRect = GuideRectFor(frame);
        Cv2.Rectangle(frame, guideRect, new Scalar(0, 165, 255), 2, LineTypes.AntiAlias);

        var faceRect = DetectFaceLikeRegion(frame);
        if (faceRect is null)
        {
            faceInGuideFrame = false;
            return ViewModel?.Localize("CaptureWorkspaceNoFaceDetected") ?? string.Empty;
        }

        var face = faceRect.Value;
        var faceCenter = new OpenCvSharp.Point(face.X + face.Width / 2, face.Y + face.Height / 2);
        var overlapRatio = CalculateOverlapRatio(face, guideRect);
        var isInside = guideRect.Contains(faceCenter) && overlapRatio >= 0.85;

        faceInGuideFrame = isInside;
        if (isInside)
        {
            lastFaceOkAt = DateTime.Now;
        }

        Cv2.Rectangle(frame, face, isInside ? new Scalar(60, 220, 60) : new Scalar(0, 0, 255), 2, LineTypes.AntiAlias);
        return isInside
            ? ViewModel?.Localize("CaptureWorkspaceFaceInsideFrame") ?? string.Empty
            : ViewModel?.Localize("CaptureWorkspaceMoveFaceIntoFrame") ?? string.Empty;
    }

    private static OpenCvSharp.Rect GuideRectFor(Mat frame)
    {
        var width = frame.Width;
        var height = frame.Height;
        var guideWidth = (int)(width * 0.62);
        var guideHeight = (int)(height * 0.88);
        return new OpenCvSharp.Rect((width - guideWidth) / 2, (height - guideHeight) / 2, guideWidth, guideHeight);
    }

    private static double CalculateOverlapRatio(OpenCvSharp.Rect face, OpenCvSharp.Rect guide)
    {
        var left = Math.Max(face.Left, guide.Left);
        var top = Math.Max(face.Top, guide.Top);
        var right = Math.Min(face.Right, guide.Right);
        var bottom = Math.Min(face.Bottom, guide.Bottom);

        if (right <= left || bottom <= top)
        {
            return 0;
        }

        var overlapArea = (right - left) * (bottom - top);
        var faceArea = Math.Max(face.Width * face.Height, 1);
        return overlapArea / (double)faceArea;
    }

    private static OpenCvSharp.Rect? DetectFaceLikeRegion(Mat frame)
    {
        // 轻量级肤色检测：适合当前原型阶段的实时提示，不等同于正式人脸识别算法。
        // 复杂光照、遮挡、背景肤色物体都可能影响结果。
        using var ycrcb = new Mat();
        using var mask = new Mat();
        using var kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new OpenCvSharp.Size(7, 7));

        Cv2.CvtColor(frame, ycrcb, ColorConversionCodes.BGR2YCrCb);
        Cv2.InRange(ycrcb, new Scalar(0, 133, 77), new Scalar(255, 173, 127), mask);
        Cv2.MorphologyEx(mask, mask, MorphTypes.Open, kernel);
        Cv2.MorphologyEx(mask, mask, MorphTypes.Close, kernel);
        Cv2.GaussianBlur(mask, mask, new OpenCvSharp.Size(5, 5), 0);

        Cv2.FindContours(mask, out OpenCvSharp.Point[][] contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

        OpenCvSharp.Rect? bestRect = null;
        var bestArea = 0d;
        var minArea = frame.Width * frame.Height * 0.015;
        foreach (var contour in contours)
        {
            var rect = Cv2.BoundingRect(contour);
            var area = rect.Width * rect.Height;
            if (area < minArea)
            {
                continue;
            }

            var ratio = rect.Width / (double)Math.Max(rect.Height, 1);
            if (ratio < 0.45 || ratio > 1.6)
            {
                continue;
            }

            if (area > bestArea)
            {
                bestArea = area;
                bestRect = rect;
            }
        }

        return bestRect;
    }

    private async Task BeginModuleRecordingSessionAsync(AssessmentCaptureViewModel viewModel)
    {
        // View 只组织当前模块上下文，实际文件路径、音视频录制和数据库记录交给录制服务。
        var sessionName = await viewModel.GetOrStartUnifiedSessionKeyAsync();
        var moduleCode = viewModel.CurrentModuleCode;
        var outputRoot = CaptureOutputPathProvider.GetOutputRoot();

        var session = await viewModel.CaptureMediaRecorder.StartAsync(new CaptureRecordingRequest(
            outputRoot,
            sessionName,
            moduleCode,
            viewModel.CurrentModule,
            viewModel.SelectedCameraDevice));

        viewModel.BeginFrameSaving(session.OutputDirectory);
    }

    private static void StopModuleRecording(AssessmentCaptureViewModel viewModel, string status, string message)
    {
        // 先更新界面上的保存状态，再让录制服务异步完成合成或丢弃。
        if (status == "discarded")
        {
            viewModel.DiscardFrameSavingStatus();
        }
        else
        {
            viewModel.StopFrameSaving();
        }

        viewModel.CaptureMediaRecorder.RequestStop(status, message);
    }

    private void RecordFrameIfNeeded(Mat frame)
    {
        var viewModel = ViewModel;
        if (viewModel is null || !viewModel.CaptureMediaRecorder.IsRecording)
        {
            return;
        }

        if (!viewModel.IsCalibrationStage)
        {
            // 第三步结束后，下一帧到来时触发录制收尾。
            // 所有任务类模块共用该完成入口，具体流程状态由各模块 ViewModel 维护。
            StopModuleRecording(viewModel, "completed", viewModel.Localize("CaptureWorkspaceModuleMediaCompleted", viewModel.CurrentModule));
            return;
        }

        if (viewModel.IsSyncTestModule && !viewModel.IsSyncTestRecordingActive)
        {
            return;
        }

        viewModel.CaptureMediaRecorder.RecordFrame(frame);
        viewModel.RecordSavedFrame();
    }

}
