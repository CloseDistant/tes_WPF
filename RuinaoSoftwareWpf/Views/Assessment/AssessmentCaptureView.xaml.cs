using System.Windows.Controls;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using RuinaoSoftwareWpf.ApplicationContracts;

namespace RuinaoSoftwareWpf.Views;

/// <summary>
/// 采集工作台页面。
/// View 层负责按钮事件、演示视频播放、摄像头预览和人脸框绘制；
/// 模块流程状态在 ViewModel 中维护，音视频录制由 ICaptureMediaService 服务处理。
/// </summary>
public partial class AssessmentCaptureView : UserControl
{
    private readonly DispatcherTimer playbackTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(300)
    };

    private readonly DispatcherTimer cameraTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(33)
    };

    private readonly SemaphoreSlim cameraLifecycleGate = new(1, 1);
    private bool cameraPreviewHasFrame;
    private DateTime lastRecordingStatusUpdateAt = DateTime.MinValue;
    private DateTimeOffset lastCameraPreviewCapturedAt = DateTimeOffset.MinValue;
    private CameraPreviewOverlayState? latestCameraOverlay;
    private WriteableBitmap? cameraPreviewBitmap;
    private AssessmentCaptureViewModel? calibrationAnimationViewModel;
    private bool hasCalibrationMarkerPosition;

    public AssessmentCaptureView()
    {
        InitializeComponent();
        playbackTimer.Tick += (_, _) => UpdatePlaybackTime();
        cameraTimer.Tick += (_, _) => UpdateCameraPreview();
        DataContextChanged += AssessmentCaptureView_DataContextChanged;
        Loaded += AssessmentCaptureView_Loaded;
        Unloaded += AssessmentCaptureView_Unloaded;
    }

    private AssessmentCaptureViewModel? ViewModel => DataContext as AssessmentCaptureViewModel;

    private async void AssessmentCaptureView_Loaded(object sender, RoutedEventArgs e)
    {
        AttachCalibrationAnimationViewModel(ViewModel);
        // 如果用户离开演示播放页后又返回，MediaElement 不会自动恢复画面。
        // 这里兜底清理“播放中但没有播放器上下文”的状态，让用户重新完整观看演示。
        ViewModel?.CancelDemoPlaybackForNavigation();

        // 摄像头冷启动通常需要数秒。进入页面后立即开始预热，并与患者评估上下文
        // 加载并行执行，避免先加载评估、再串行等待摄像头造成额外延迟。
        var cameraPreviewTask = StartCameraPreviewAsync();
        if (ViewModel is { } viewModel)
        {
            try
            {
                await viewModel.EnterWorkbenchAsync();
            }
            catch (Exception exception)
            {
                viewModel.ShowStageNotice($"加载患者评估进度失败：{exception.Message}");
            }
        }

        await cameraPreviewTask;
    }

    private async void AssessmentCaptureView_Unloaded(object sender, RoutedEventArgs e)
    {
        await StopPageActivitiesForUnloadAsync();
    }

    private async Task StopPageActivitiesForUnloadAsync()
    {
        DetachCalibrationAnimationViewModel();
        StopCalibrationMarkerAnimation();
        playbackTimer.Stop();
        DemoMedia.Stop();
        VideoBrowseMedia.Stop();
        ViewModel?.CancelDemoPlaybackForNavigation();
        await StopCameraPreviewAsync();
    }

    private void AssessmentCaptureView_DataContextChanged(
        object sender,
        DependencyPropertyChangedEventArgs e)
    {
        DetachCalibrationAnimationViewModel();
        if (IsLoaded)
        {
            AttachCalibrationAnimationViewModel(e.NewValue as AssessmentCaptureViewModel);
        }
    }

    private void AttachCalibrationAnimationViewModel(AssessmentCaptureViewModel? viewModel)
    {
        if (viewModel is null || ReferenceEquals(calibrationAnimationViewModel, viewModel))
        {
            return;
        }

        calibrationAnimationViewModel = viewModel;
        calibrationAnimationViewModel.PropertyChanged += OnCalibrationViewModelPropertyChanged;
        SetCalibrationMarkerPosition(
            viewModel.CalibrationCanvasLeft,
            viewModel.CalibrationCanvasTop);
        hasCalibrationMarkerPosition = true;
    }

    private void DetachCalibrationAnimationViewModel()
    {
        if (calibrationAnimationViewModel is not null)
        {
            calibrationAnimationViewModel.PropertyChanged -= OnCalibrationViewModelPropertyChanged;
            calibrationAnimationViewModel = null;
        }

        hasCalibrationMarkerPosition = false;
    }

    private void OnCalibrationViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not nameof(AssessmentCaptureViewModel.CalibrationAnimationSequence))
        {
            return;
        }

        Dispatcher.BeginInvoke(
            DispatcherPriority.Render,
            new Action(ApplyCalibrationMarkerAnimation));
    }

    private void ApplyCalibrationMarkerAnimation()
    {
        var viewModel = calibrationAnimationViewModel;
        if (viewModel is null || !viewModel.IsCalibrationMarkerVisible)
        {
            StopCalibrationMarkerAnimation();
            hasCalibrationMarkerPosition = false;
            return;
        }

        var targetLeft = viewModel.CalibrationCanvasLeft;
        var targetTop = viewModel.CalibrationCanvasTop;
        if (!hasCalibrationMarkerPosition || viewModel.CalibrationMoveDurationMilliseconds <= 0)
        {
            SetCalibrationMarkerPosition(targetLeft, targetTop);
            hasCalibrationMarkerPosition = true;
            return;
        }

        var startX = CalibrationMarkerTransform.X;
        var startY = CalibrationMarkerTransform.Y;
        var duration = TimeSpan.FromMilliseconds(viewModel.CalibrationMoveDurationMilliseconds);
        var easing = new CubicEase { EasingMode = EasingMode.EaseInOut };

        CalibrationMarkerTransform.BeginAnimation(TranslateTransform.XProperty, null);
        CalibrationMarkerTransform.BeginAnimation(TranslateTransform.YProperty, null);
        CalibrationMarkerTransform.X = targetLeft;
        CalibrationMarkerTransform.Y = targetTop;
        CalibrationMarkerTransform.BeginAnimation(
            TranslateTransform.XProperty,
            new DoubleAnimation(startX, targetLeft, duration)
            {
                EasingFunction = easing,
                FillBehavior = FillBehavior.Stop
            });
        CalibrationMarkerTransform.BeginAnimation(
            TranslateTransform.YProperty,
            new DoubleAnimation(startY, targetTop, duration)
            {
                EasingFunction = easing,
                FillBehavior = FillBehavior.Stop
            });

        hasCalibrationMarkerPosition = true;
    }

    private void StopCalibrationMarkerAnimation()
    {
        CalibrationMarkerTransform.BeginAnimation(TranslateTransform.XProperty, null);
        CalibrationMarkerTransform.BeginAnimation(TranslateTransform.YProperty, null);
        CalibrationMarkerTransform.X = 0;
        CalibrationMarkerTransform.Y = 0;
    }

    private void SetCalibrationMarkerPosition(double left, double top)
    {
        CalibrationMarkerTransform.BeginAnimation(TranslateTransform.XProperty, null);
        CalibrationMarkerTransform.BeginAnimation(TranslateTransform.YProperty, null);
        CalibrationMarkerTransform.X = left;
        CalibrationMarkerTransform.Y = top;
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

    private async void SkipDemoButton_Click(object sender, RoutedEventArgs e)
    {
        var viewModel = ViewModel;
        if (viewModel is null || !viewModel.SkipDemoForDevelopment())
        {
            return;
        }

        playbackTimer.Stop();
        DemoMedia.Stop();
        DemoMedia.Position = TimeSpan.Zero;
        await StartCameraPreviewAsync();
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
            await StartCameraPreviewAsync();
            return;
        }

        if (!viewModel.HasSelectedCamera)
        {
            viewModel.ShowStageNotice(viewModel.Localize("CaptureWorkspaceNoCameraStageNotice"));
            return;
        }

        if (!cameraPreviewHasFrame)
        {
            await StartCameraPreviewAsync();
            viewModel.ShowStageNotice(viewModel.Localize("CaptureWorkspaceCameraNoFrameStageNotice"));
            return;
        }

        if (!viewModel.IsFaceReady)
        {
            viewModel.ShowStageNotice(viewModel.Localize("CaptureWorkspaceFaceNotReadyStageNotice"));
            return;
        }

        try
        {
            await BeginModuleRecordingSessionAsync(viewModel);
        }
        catch (AssessmentModuleStartException exception)
        {
            viewModel.ShowStageNotice($"当前评估模块无法启动：{exception.InnerException?.Message ?? exception.Message}");
            return;
        }
        catch (Exception exception)
        {
            StopModuleRecording(viewModel, CaptureMediaStopReason.Failed, viewModel.Localize("CaptureWorkspaceMediaStartFailed", exception.Message));
            await viewModel.FailCurrentModuleAttemptAsync("MEDIA_START_FAILED", exception.Message);
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
            await StartCameraPreviewAsync();
            viewModel.ShowStageNotice(viewModel.Localize("CaptureWorkspaceCameraNoFrameStageNotice"));
            return;
        }

        try
        {
            await BeginModuleRecordingSessionAsync(viewModel);
        }
        catch (AssessmentModuleStartException exception)
        {
            viewModel.ShowStageNotice($"当前评估模块无法启动：{exception.InnerException?.Message ?? exception.Message}");
            return;
        }
        catch (Exception exception)
        {
            StopModuleRecording(viewModel, CaptureMediaStopReason.Failed, viewModel.Localize("CaptureWorkspaceSyncTestStartFailed", exception.Message));
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

    private async void RefreshCameraButton_Click(object sender, RoutedEventArgs e)
    {
        await StartCameraPreviewAsync(forceReopen: true);
    }

    private async void CameraComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded)
        {
            await StartCameraPreviewAsync();
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

    private async Task StartCameraPreviewAsync(bool forceReopen = false)
    {
        await cameraLifecycleGate.WaitAsync();
        try
        {
            var viewModel = ViewModel;
            if (viewModel is null || !viewModel.HasSelectedCamera)
            {
                await StopCameraPreviewCoreAsync();
                CameraPreviewStatusText.Text = viewModel?.Localize("CaptureWorkspaceNoCameraSelected")
                    ?? string.Empty;
                return;
            }

            cameraTimer.Stop();
            if (viewModel.IsMediaRecording)
            {
                StopRecordingForPreviewStop();
            }

            var cameraIndex = CameraComboBox.SelectedIndex < 0 ? 0 : CameraComboBox.SelectedIndex;
            CameraPreviewStatusText.Text = viewModel.Localize("CaptureWorkspaceOpeningCamera");
            if (!await viewModel.OpenCameraAsync(cameraIndex, forceReopen))
            {
                ResetCameraPreviewDisplay();
                CameraPreviewStatusText.Text = viewModel.CameraOpenFailureMessage;
                return;
            }

            cameraTimer.Start();
        }
        catch (Exception exception)
        {
            CameraPreviewStatusText.Text = ViewModel?.Localize("CaptureWorkspaceCameraOpenFailed")
                ?? exception.Message;
        }
        finally
        {
            cameraLifecycleGate.Release();
        }
    }

    private async Task StopCameraPreviewAsync()
    {
        await cameraLifecycleGate.WaitAsync();
        try
        {
            await StopCameraPreviewCoreAsync();
        }
        finally
        {
            cameraLifecycleGate.Release();
        }
    }

    private async Task StopCameraPreviewCoreAsync()
    {
        cameraTimer.Stop();
        StopRecordingForPreviewStop();
        if (ViewModel is { } viewModel)
        {
            await viewModel.CloseCameraAsync();
        }

        ResetCameraPreviewDisplay();
    }

    private void ResetCameraPreviewDisplay()
    {
        CameraPreviewImage.Source = null;
        cameraPreviewBitmap = null;
        CameraGuideRectangle.Visibility = Visibility.Collapsed;
        CameraFaceRectangle.Visibility = Visibility.Collapsed;
        cameraPreviewHasFrame = false;
        latestCameraOverlay = null;
        (DataContext as AssessmentCaptureViewModel)?.ResetFaceReadiness();
        (DataContext as AssessmentCaptureViewModel)?.ResetFaceConditionMonitoring();
        lastCameraPreviewCapturedAt = DateTimeOffset.MinValue;
        lastRecordingStatusUpdateAt = DateTime.MinValue;
    }

    private void StopRecordingForPreviewStop()
    {
        var viewModel = ViewModel;
        if (viewModel is null || !viewModel.IsMediaRecording)
        {
            return;
        }

        if (viewModel.IsCompletionStage)
        {
            // 模块已经正常完成时，离开页面只触发正常收尾和合成。
            StopModuleRecording(viewModel, CaptureMediaStopReason.Completed, viewModel.Localize("CaptureWorkspaceModuleMediaCompleted", viewModel.CurrentModule));
            return;
        }

        // 第三步正式采集中切换页面或关闭程序，视为中断。
        // 中断数据不合成、不作为有效记录，录制服务会尝试删除临时音视频文件。
        var message = viewModel.Localize("CaptureWorkspaceRecordingInterruptedMessage");
        viewModel.DiscardCurrentModuleExecution(message);
    }

    private void UpdateCameraPreview()
    {
        var viewModel = ViewModel;
        if (viewModel is null || !viewModel.IsCameraOpen)
        {
            return;
        }

        if (!viewModel.TryTakeLatestCameraPreview(out var snapshot))
        {
            if (!cameraPreviewHasFrame
                || DateTimeOffset.Now - lastCameraPreviewCapturedAt > TimeSpan.FromSeconds(1))
            {
                viewModel.ResetFaceReadiness();
                CameraFaceRectangle.Visibility = Visibility.Collapsed;
                CameraPreviewStatusText.Text = viewModel.Localize("CaptureWorkspaceNoFrameRead");
            }

            return;
        }

        using (snapshot)
        {
            if (DateTimeOffset.Now - snapshot.CapturedAt > TimeSpan.FromSeconds(1))
            {
                viewModel.ResetFaceReadiness();
                CameraFaceRectangle.Visibility = Visibility.Collapsed;
                CameraPreviewStatusText.Text = viewModel.Localize("CaptureWorkspaceNoFrameRead");
                return;
            }

            if (cameraPreviewBitmap is null
                || cameraPreviewBitmap.PixelWidth != snapshot.Width
                || cameraPreviewBitmap.PixelHeight != snapshot.Height)
            {
                cameraPreviewBitmap = new WriteableBitmap(
                    snapshot.Width,
                    snapshot.Height,
                    96,
                    96,
                    PixelFormats.Bgra32,
                    null);
                CameraPreviewImage.Source = cameraPreviewBitmap;
            }

            cameraPreviewBitmap.WritePixels(
                new Int32Rect(0, 0, snapshot.Width, snapshot.Height),
                snapshot.BgraPixels,
                snapshot.Stride,
                0);
            latestCameraOverlay = new CameraPreviewOverlayState(
                snapshot.Width,
                snapshot.Height,
                snapshot.GuideBounds,
                snapshot.FaceBounds,
                snapshot.FaceState,
                snapshot.IsPrimaryFaceInsideGuide);
            lastCameraPreviewCapturedAt = snapshot.CapturedAt;
            UpdateCameraOverlay(latestCameraOverlay.Value);

            cameraPreviewHasFrame = true;
            var nowTimestamp = Stopwatch.GetTimestamp();
            viewModel.ObserveFaceReadiness(
                snapshot.FaceState,
                snapshot.IsPrimaryFaceInsideGuide,
                nowTimestamp);

            var faceStatusText = FaceStateText(viewModel, snapshot.FaceState);
            if (snapshot.FaceState == CameraFaceState.Normal && !snapshot.IsPrimaryFaceInsideGuide)
            {
                faceStatusText = viewModel.Localize("CaptureWorkspaceMoveFaceIntoFrame");
            }

            var shouldMonitorFace = viewModel.IsExecutingCaptureTask
                && viewModel.IsMediaRecording
                && !viewModel.IsSyncTestModule;
            var monitorUpdate = viewModel.ObserveFaceCondition(snapshot.FaceState, nowTimestamp);
            if (shouldMonitorFace)
            {
                if (!monitorUpdate.IsNormal)
                {
                    faceStatusText = viewModel.Localize(
                        "CaptureWorkspaceFaceAbnormalCountdown",
                        faceStatusText,
                        monitorUpdate.AbnormalDuration.TotalSeconds);
                }

            }

            CameraPreviewStatusText.Text = faceStatusText;

            if (viewModel.IsMediaRecording
                && DateTime.Now - lastRecordingStatusUpdateAt >= TimeSpan.FromMilliseconds(500))
            {
                viewModel.UpdateRecordedFrameCount(snapshot.RecordedFrameCount);
                lastRecordingStatusUpdateAt = DateTime.Now;
            }
        }
    }

    private void CameraPreviewViewport_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (latestCameraOverlay is { } snapshot)
        {
            UpdateCameraOverlay(snapshot);
        }
    }

    private void UpdateCameraOverlay(CameraPreviewOverlayState snapshot)
    {
        var viewportWidth = CameraPreviewViewport.ActualWidth;
        var viewportHeight = CameraPreviewViewport.ActualHeight;
        if (viewportWidth <= 0 || viewportHeight <= 0 || snapshot.Width <= 0 || snapshot.Height <= 0)
        {
            return;
        }

        var scale = Math.Max(viewportWidth / snapshot.Width, viewportHeight / snapshot.Height);
        var offsetX = (viewportWidth - snapshot.Width * scale) / 2d;
        var offsetY = (viewportHeight - snapshot.Height * scale) / 2d;
        PositionOverlayRectangle(CameraGuideRectangle, snapshot.GuideBounds, scale, offsetX, offsetY, snapshot);
        CameraGuideRectangle.Visibility = Visibility.Visible;

        if (snapshot.FaceBounds is not { } faceBounds)
        {
            CameraFaceRectangle.Visibility = Visibility.Collapsed;
            return;
        }

        PositionOverlayRectangle(CameraFaceRectangle, faceBounds, scale, offsetX, offsetY, snapshot);
        CameraFaceRectangle.Stroke = snapshot.FaceState == CameraFaceState.Normal
            && snapshot.IsPrimaryFaceInsideGuide
            ? Brushes.LimeGreen
            : Brushes.Red;
        CameraFaceRectangle.Visibility = Visibility.Visible;
    }

    private static void PositionOverlayRectangle(
        System.Windows.Shapes.Rectangle rectangle,
        NormalizedCameraRect bounds,
        double scale,
        double offsetX,
        double offsetY,
        CameraPreviewOverlayState snapshot)
    {
        Canvas.SetLeft(rectangle, offsetX + bounds.X * snapshot.Width * scale);
        Canvas.SetTop(rectangle, offsetY + bounds.Y * snapshot.Height * scale);
        rectangle.Width = bounds.Width * snapshot.Width * scale;
        rectangle.Height = bounds.Height * snapshot.Height * scale;
    }

    private readonly record struct CameraPreviewOverlayState(
        int Width,
        int Height,
        NormalizedCameraRect GuideBounds,
        NormalizedCameraRect? FaceBounds,
        CameraFaceState FaceState,
        bool IsPrimaryFaceInsideGuide);

    private static string FaceStateText(
        AssessmentCaptureViewModel viewModel,
        CameraFaceState state) => state switch
        {
            CameraFaceState.Normal => viewModel.Localize("CaptureWorkspaceFaceInsideFrame"),
            CameraFaceState.NoFace => viewModel.Localize("CaptureWorkspaceNoFaceDetected"),
            CameraFaceState.MultipleFaces => viewModel.Localize("CaptureWorkspaceMultipleFaces"),
            CameraFaceState.FaceOccluded => viewModel.Localize("CaptureWorkspaceFaceOccluded"),
            CameraFaceState.EyesNotVisible => viewModel.Localize("CaptureWorkspaceEyesNotVisible"),
            CameraFaceState.EyesClosed => viewModel.Localize("CaptureWorkspaceEyesClosed"),
            CameraFaceState.MouthNotVisible => viewModel.Localize("CaptureWorkspaceMouthNotVisible"),
            CameraFaceState.HeadPoseInvalid => viewModel.Localize("CaptureWorkspaceHeadPoseInvalid"),
            _ => viewModel.Localize("CaptureWorkspaceFaceDetectorUnavailable")
        };

    private async Task BeginModuleRecordingSessionAsync(AssessmentCaptureViewModel viewModel)
    {
        // View 只组织当前模块上下文，实际文件路径、音视频录制和数据库记录交给录制服务。
        var sessionName = await viewModel.GetOrStartUnifiedSessionKeyAsync();
        var moduleCode = viewModel.CurrentModuleCode;
        long? assessmentAttemptId = null;
        if (!viewModel.IsSyncTestModule && !viewModel.IsDevelopmentModuleOverride)
        {
            try
            {
                assessmentAttemptId = (await viewModel.BeginCurrentModuleAttemptAsync(sessionName)).AttemptId;
            }
            catch (Exception exception)
            {
                throw new AssessmentModuleStartException(exception);
            }
        }

        var session = await viewModel.StartMediaRecordingAsync(new CaptureMediaStartRequest(
            assessmentAttemptId,
            sessionName,
            moduleCode,
            viewModel.CurrentModule,
            viewModel.SelectedCameraDevice));

        viewModel.BeginFrameSaving(session.OutputDirectory);
    }

    private sealed class AssessmentModuleStartException(Exception innerException)
        : Exception("正式评估模块上下文无效。", innerException);

    private static void StopModuleRecording(
        AssessmentCaptureViewModel viewModel,
        CaptureMediaStopReason reason,
        string message)
    {
        // 先更新界面上的保存状态，再让录制服务异步完成合成或丢弃。
        if (reason == CaptureMediaStopReason.Discarded)
        {
            viewModel.DiscardFrameSavingStatus();
        }
        else
        {
            viewModel.StopFrameSaving();
        }

        viewModel.RequestMediaStop(reason, message);
    }

}
