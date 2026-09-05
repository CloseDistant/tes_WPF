using System.Windows.Controls;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Windows.Input;
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
        Interval = TimeSpan.FromMilliseconds(50)
    };

    private readonly DispatcherTimer faceStatusTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(200)
    };

    private readonly SemaphoreSlim cameraLifecycleGate = new(1, 1);
    private bool cameraPreviewHasFrame;
    private DateTime lastRecordingStatusUpdateAt = DateTime.MinValue;
    private DateTimeOffset lastCameraPreviewCapturedAt = DateTimeOffset.MinValue;
    private DateTimeOffset lastCameraFaceStatusCapturedAt = DateTimeOffset.MinValue;
    private CameraPreviewOverlayState? latestCameraOverlay;
    private WriteableBitmap? cameraPreviewBitmap;
    private AssessmentCaptureViewModel? calibrationAnimationViewModel;
    private bool hasCalibrationMarkerPosition;
    private bool isFormalPresentationActive;
    private bool isStartingFormalPresentation;
    private bool isCaptureDetailsOpen = true;
    private bool captureDetailsOpenBeforeFormal = true;
    private Window? keyboardOwnerWindow;
    private SpaceShortcutSnapshot? armedSpaceShortcut;

    private enum SpaceShortcutAction
    {
        PlayDemo,
        EnterFaceCheck,
        StartFormalModule,
        StartVoiceBaseline,
        FinishVoiceBaseline,
        StartWordReading,
        StartShortTextReading,
        StartEmotionQuestion,
        CompleteEmotionQuestion,
        StartSyncTest,
        RetryModule,
        NextModule
    }

    private sealed record SpaceShortcutSnapshot(
        SpaceShortcutAction Action,
        int ModuleTypeId,
        string StepText);

    public AssessmentCaptureView()
    {
        InitializeComponent();
        playbackTimer.Tick += (_, _) => UpdatePlaybackTime();
        cameraTimer.Tick += (_, _) => UpdateCameraPreview();
        faceStatusTimer.Tick += (_, _) => UpdateCameraFaceStatus();
        DataContextChanged += AssessmentCaptureView_DataContextChanged;
        Loaded += AssessmentCaptureView_Loaded;
        Unloaded += AssessmentCaptureView_Unloaded;
        WorkbenchContent.SizeChanged += (_, _) => UpdateFormalParadigmBounds();
    }

    private AssessmentCaptureViewModel? ViewModel => DataContext as AssessmentCaptureViewModel;

    private async void AssessmentCaptureView_Loaded(object sender, RoutedEventArgs e)
    {
        AttachKeyboardShortcutHandler();
        CompositionTarget.Rendering += OnCompositionTargetRendering;
        // 将键盘焦点放在采集工作台根节点，避免按钮/MediaElement 保留焦点时
        // 字母键只进入控件内部而没有进入任务快捷键处理链。
        Focus();
        ConfigureDevelopmentDetails();
        SetCaptureDetailsOpen(true);
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
        CompositionTarget.Rendering -= OnCompositionTargetRendering;
        DetachKeyboardShortcutHandler();
        await StopPageActivitiesForUnloadAsync();
    }

    private void OnCompositionTargetRendering(object? sender, EventArgs e)
    {
        // 以真正进入渲染管线的首帧作为刺激起始时刻；ViewModel 仍保留状态切换时刻兜底。
        ViewModel?.MarkEmotionStroopStimulusRendered();
    }

    private void AttachKeyboardShortcutHandler()
    {
        DetachKeyboardShortcutHandler();
        keyboardOwnerWindow = Window.GetWindow(this);
        if (keyboardOwnerWindow is not null)
        {
            // 用 AddHandler 并打开 handledEventsToo：媒体控件、按钮和输入框可能先把
            // PreviewKeyDown 标记为 Handled，普通事件订阅在这种情况下收不到按键。
            keyboardOwnerWindow.AddHandler(
                Keyboard.PreviewKeyDownEvent,
                new KeyEventHandler(OnOwnerWindowPreviewKeyDown),
                handledEventsToo: true);
            keyboardOwnerWindow.AddHandler(
                Keyboard.PreviewKeyUpEvent,
                new KeyEventHandler(OnOwnerWindowPreviewKeyUp),
                handledEventsToo: true);
        }
    }

    private void DetachKeyboardShortcutHandler()
    {
        if (keyboardOwnerWindow is not null)
        {
            keyboardOwnerWindow.RemoveHandler(
                Keyboard.PreviewKeyDownEvent,
                new KeyEventHandler(OnOwnerWindowPreviewKeyDown));
            keyboardOwnerWindow.RemoveHandler(
                Keyboard.PreviewKeyUpEvent,
                new KeyEventHandler(OnOwnerWindowPreviewKeyUp));
            keyboardOwnerWindow = null;
        }

        armedSpaceShortcut = null;
    }

    private void OnOwnerWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Stroop 的 F/J 是正式刺激反应键，必须优先于隐藏控件遗留的键盘焦点处理。
        // KeyUp 还会再次兜底，避免某些媒体控件只放行按键释放事件。
        if (TryHandleEmotionStroopKey(e))
        {
            return;
        }

        if (e.Key != Key.Space || e.IsRepeat || IsTextInputFocused())
        {
            return;
        }

        e.Handled = true;
        if (armedSpaceShortcut is not null || ViewModel is not { } viewModel)
        {
            return;
        }

        if (TryGetSpaceShortcutAction(viewModel, out var action))
        {
            armedSpaceShortcut = new SpaceShortcutSnapshot(
                action,
                viewModel.CurrentModuleTypeId,
                viewModel.CurrentDevStepText);
        }
    }

    private async void OnOwnerWindowPreviewKeyUp(object sender, KeyEventArgs e)
    {
        // 部分 WPF 原生媒体/输入控件会吞掉字母键的 KeyDown，但仍会冒泡 KeyUp；
        // 因此 F/J 必须在释放事件再次尝试，ViewModel 内部保证只记录第一次有效反应。
        if (TryHandleEmotionStroopKey(e))
        {
            return;
        }

        if (e.Key != Key.Space)
        {
            return;
        }

        var snapshot = armedSpaceShortcut;
        armedSpaceShortcut = null;
        if (IsTextInputFocused())
        {
            return;
        }

        e.Handled = true;
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        // 按钮获得焦点时，部分 WPF 控件会吞掉 PreviewKeyDown，导致没有快照；
        // KeyUp 仍是一次明确的用户操作，此时直接按当前可提交状态完成本题。
        if (snapshot is null && viewModel.CanCompleteEmotionQuestionAnswer)
        {
            await ExecuteSpaceShortcutAsync(SpaceShortcutAction.CompleteEmotionQuestion, viewModel);
            return;
        }

        if (snapshot is null
            || viewModel.CurrentModuleTypeId != snapshot.ModuleTypeId
            || !string.Equals(viewModel.CurrentDevStepText, snapshot.StepText, StringComparison.Ordinal)
            || !TryGetSpaceShortcutAction(viewModel, out var currentAction)
            || currentAction != snapshot.Action)
        {
            return;
        }

        await ExecuteSpaceShortcutAsync(snapshot.Action, viewModel);
    }

    private bool TryHandleEmotionStroopKey(KeyEventArgs e)
    {
        if (e.IsRepeat || ViewModel is not { IsEmotionStroopModule: true, IsEmotionStroopStimulusVisible: true } stroopViewModel)
        {
            return false;
        }

        // Key.System 出现在 Alt 组合键中，普通 F/J 则直接使用 Key。
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var response = key switch
        {
            Key.F => EmotionStroopResponse.Positive,
            Key.J => EmotionStroopResponse.Negative,
            _ => (EmotionStroopResponse?)null
        };
        if (response is null)
        {
            return false;
        }

        e.Handled = true;
        stroopViewModel.SubmitEmotionStroopKeyboardResponse(response.Value);
        return true;
    }

    private static bool IsTextInputFocused()
    {
        var focused = Keyboard.FocusedElement;
        return focused is System.Windows.Controls.Primitives.TextBoxBase
            or PasswordBox
            or ComboBox;
    }

    private static bool TryGetSpaceShortcutAction(
        AssessmentCaptureViewModel viewModel,
        out SpaceShortcutAction action)
    {
        if (viewModel.ShowDemoPlayAction)
        {
            action = SpaceShortcutAction.PlayDemo;
            return true;
        }

        if (viewModel.ShowEmotionStroopPracticeStartAction)
        {
            action = SpaceShortcutAction.StartFormalModule;
            return true;
        }

        if (viewModel.ShowEmotionStroopFormalStartAction)
        {
            action = SpaceShortcutAction.StartFormalModule;
            return true;
        }

        if (viewModel.ShowFaceCheckAction)
        {
            action = SpaceShortcutAction.EnterFaceCheck;
            return true;
        }

        if (viewModel.IsFaceStep && viewModel.IsFaceReady)
        {
            action = SpaceShortcutAction.StartFormalModule;
            return true;
        }

        if (viewModel.ShowVoiceBaselineStartAction)
        {
            action = SpaceShortcutAction.StartVoiceBaseline;
            return true;
        }

        if (viewModel.CanFinishVoiceBaselineSegment)
        {
            action = SpaceShortcutAction.FinishVoiceBaseline;
            return true;
        }

        if (viewModel.ShowWordReadingStartAction)
        {
            action = SpaceShortcutAction.StartWordReading;
            return true;
        }

        if (viewModel.ShowShortTextReadingStartAction)
        {
            action = SpaceShortcutAction.StartShortTextReading;
            return true;
        }

        if (viewModel.IsEmotionQuestionWaiting)
        {
            action = SpaceShortcutAction.StartEmotionQuestion;
            return true;
        }

        if (viewModel.CanCompleteEmotionQuestionAnswer)
        {
            action = SpaceShortcutAction.CompleteEmotionQuestion;
            return true;
        }

        if (viewModel.ShowSyncTestStartAction)
        {
            action = SpaceShortcutAction.StartSyncTest;
            return true;
        }

        if (viewModel.IsModuleSaveFailed && viewModel.RetryFailedModuleCommand.CanExecute(null))
        {
            action = SpaceShortcutAction.RetryModule;
            return true;
        }

        if (viewModel.IsCompletionStage && viewModel.GoNextModuleCommand.CanExecute(null))
        {
            action = SpaceShortcutAction.NextModule;
            return true;
        }

        action = default;
        return false;
    }

    private async Task ExecuteSpaceShortcutAsync(
        SpaceShortcutAction action,
        AssessmentCaptureViewModel viewModel)
    {
        switch (action)
        {
            case SpaceShortcutAction.PlayDemo:
                PlayDemoButton_Click(this, new RoutedEventArgs());
                break;
            case SpaceShortcutAction.EnterFaceCheck:
            case SpaceShortcutAction.StartFormalModule:
                if (viewModel.ShowEmotionStroopPracticeStartAction)
                {
                    viewModel.StartEmotionStroopPracticeCommand.Execute(null);
                }
                else if (viewModel.ShowEmotionStroopFormalStartAction)
                {
                    await StartEmotionStroopFormalButtonAsync(viewModel);
                }
                else
                {
                    await StartCalibrationButtonAsync(viewModel);
                }
                break;
            case SpaceShortcutAction.StartVoiceBaseline:
                await StartVoiceBaselineButtonAsync(viewModel);
                break;
            case SpaceShortcutAction.FinishVoiceBaseline:
                viewModel.FinishVoiceBaselineSegment();
                break;
            case SpaceShortcutAction.StartWordReading:
                StartWordReadingButton_Click(this, new RoutedEventArgs());
                break;
            case SpaceShortcutAction.StartShortTextReading:
                viewModel.StartShortTextReadingCommand.Execute(null);
                break;
            case SpaceShortcutAction.StartEmotionQuestion:
                viewModel.StartEmotionQuestionCommand.Execute(null);
                break;
            case SpaceShortcutAction.CompleteEmotionQuestion:
                viewModel.CompleteEmotionQuestionAnswerCommand.Execute(null);
                break;
            case SpaceShortcutAction.StartSyncTest:
                await StartSyncTestAsync(viewModel);
                break;
            case SpaceShortcutAction.RetryModule:
                viewModel.RetryFailedModuleCommand.Execute(null);
                break;
            case SpaceShortcutAction.NextModule:
                viewModel.GoNextModuleCommand.Execute(null);
                break;
        }
    }

    private async Task StopPageActivitiesForUnloadAsync()
    {
        ExitFormalPresentationMode();
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
        if (e.PropertyName == nameof(AssessmentCaptureViewModel.IsExecutingCaptureTask))
        {
            Dispatcher.BeginInvoke(
                DispatcherPriority.Render,
                new Action(UpdateFormalPresentationForCurrentStage));
        }

        if (e.PropertyName is not nameof(AssessmentCaptureViewModel.CalibrationAnimationSequence))
        {
            return;
        }

        Dispatcher.BeginInvoke(
            DispatcherPriority.Render,
            new Action(ApplyCalibrationMarkerAnimation));
    }

    private void ConfigureDevelopmentDetails()
    {
#if DEBUG
        DevelopmentModuleProgressCard.Visibility = Visibility.Visible;
#else
        DevelopmentModuleProgressCard.Visibility = Visibility.Collapsed;
#endif
    }

    private void HideCaptureDetailsButton_Click(object sender, RoutedEventArgs e)
    {
        SetCaptureDetailsOpen(false);
    }

    private void ShowCaptureDetailsButton_Click(object sender, RoutedEventArgs e)
    {
        SetCaptureDetailsOpen(true);
    }

    private void SetCaptureDetailsOpen(bool open)
    {
        isCaptureDetailsOpen = open;
        if (isFormalPresentationActive)
        {
            CaptureDetailsPanel.Visibility = Visibility.Collapsed;
            ShowCaptureDetailsButton.Visibility = Visibility.Collapsed;
            SetCameraPreviewRenderingEnabled(false);
            return;
        }

        CaptureDetailsPanel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        ShowCaptureDetailsButton.Visibility = open ? Visibility.Collapsed : Visibility.Visible;
        SetCameraPreviewRenderingEnabled(open);
    }

    private async Task EnterFormalPresentationModeAsync()
    {
        if (isFormalPresentationActive)
        {
            return;
        }

        if (Window.GetWindow(this) is not MainWindow mainWindow)
        {
            throw new InvalidOperationException("无法取得数字表型采集的主窗口。");
        }

        captureDetailsOpenBeforeFormal = isCaptureDetailsOpen;
        isFormalPresentationActive = true;
        Keyboard.ClearFocus();
        mainWindow.EnterAssessmentPresentationMode();

        WorkbenchRoot.Margin = new Thickness(0);
        WorkbenchHeader.Visibility = Visibility.Collapsed;
        ParadigmCard.Padding = new Thickness(0);
        ParadigmCard.Margin = new Thickness(0);
        ParadigmCard.BorderThickness = new Thickness(0);
        ParadigmCard.CornerRadius = new CornerRadius(0);
        SharedDisplayHeader.Visibility = Visibility.Collapsed;
        SharedDisplayFrame.BorderThickness = new Thickness(0);
        SharedDisplayFrame.CornerRadius = new CornerRadius(0);
        ParadigmCanvasHost.Margin = new Thickness(0);
        SetCaptureDetailsOpen(false);
        UpdateFormalParadigmBounds();

        // 等待无边框全屏布局真正提交后再启动范式时间轴，避免首个刺激帧在布局切换中丢失。
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
        Focus();
    }

    private void ExitFormalPresentationMode()
    {
        if (!isFormalPresentationActive)
        {
            return;
        }

        isFormalPresentationActive = false;
        if (Window.GetWindow(this) is MainWindow mainWindow)
        {
            mainWindow.ExitAssessmentPresentationMode();
        }

        WorkbenchRoot.Margin = new Thickness(18, 14, 18, 14);
        WorkbenchHeader.Visibility = Visibility.Visible;
        ParadigmCard.Width = double.NaN;
        ParadigmCard.Height = double.NaN;
        ParadigmCard.HorizontalAlignment = HorizontalAlignment.Stretch;
        ParadigmCard.VerticalAlignment = VerticalAlignment.Stretch;
        ParadigmCard.Padding = new Thickness(14);
        ParadigmCard.BorderThickness = new Thickness(1);
        ParadigmCard.CornerRadius = new CornerRadius(6);
        SharedDisplayHeader.Visibility = Visibility.Visible;
        SharedDisplayFrame.BorderThickness = new Thickness(1);
        SharedDisplayFrame.CornerRadius = new CornerRadius(4);
        ParadigmCanvasHost.Margin = new Thickness(16, 10, 16, 10);
        SetCaptureDetailsOpen(captureDetailsOpenBeforeFormal);
    }

    private async void UpdateFormalPresentationForCurrentStage()
    {
        if (ViewModel?.IsExecutingCaptureTask == true)
        {
            if (!isFormalPresentationActive)
            {
                try
                {
                    await EnterFormalPresentationModeAsync();
                }
                catch (Exception exception)
                {
                    ViewModel.ShowStageNotice($"正式采集画面启动失败：{exception.Message}");
                }
            }

            return;
        }

        // 从面部取景切换到正式任务时，会先调整窗口并等待一次 Render。
        // 此时忽略此前排队的旧阶段通知，避免它把刚进入的全屏立即恢复。
        if (!isStartingFormalPresentation)
        {
            ExitFormalPresentationMode();
        }
    }

    private void UpdateFormalParadigmBounds()
    {
        if (!isFormalPresentationActive)
        {
            return;
        }

        var availableWidth = WorkbenchContent.ActualWidth;
        var availableHeight = WorkbenchContent.ActualHeight;
        if (availableWidth <= 0 || availableHeight <= 0)
        {
            return;
        }

        // 图片浏览素材按屏幕可用区域完整适配（不裁切、不拉伸）；校准画面也复用同一全屏容器。
        if (ViewModel?.IsPictureBrowseModule == true)
        {
            ParadigmCard.Width = availableWidth;
            ParadigmCard.Height = availableHeight;
            ParadigmCard.HorizontalAlignment = HorizontalAlignment.Center;
            ParadigmCard.VerticalAlignment = VerticalAlignment.Center;

            // 图片展示区域按目标设备的物理屏幕比例设置为 50% 宽、60% 高。
            // WPF ActualWidth/ActualHeight 已是当前 DPI 下的逻辑尺寸，使用百分比
            // 可自动适配 150% 缩放及后续不同分辨率；Image 用 Uniform 保证原图比例，
            // 不拉伸、不裁切，也不会放大到超过该展示区域。
            PictureBrowseImage.Width = availableWidth * 0.5d;
            PictureBrowseImage.Height = availableHeight * 0.6d;
            return;
        }

        const double aspectRatio = 16d / 9d;
        var canvasWidth = availableWidth;
        var canvasHeight = canvasWidth / aspectRatio;
        if (canvasHeight > availableHeight)
        {
            canvasHeight = availableHeight;
            canvasWidth = canvasHeight * aspectRatio;
        }

        ParadigmCard.Width = canvasWidth;
        ParadigmCard.Height = canvasHeight;
        ParadigmCard.HorizontalAlignment = HorizontalAlignment.Center;
        ParadigmCard.VerticalAlignment = VerticalAlignment.Center;
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

    private async void PlayDemoButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var viewModel = ViewModel;
        if (viewModel is null)
        {
            return;
        }

        if (viewModel.IsInstructionStage)
        {
            // 图片浏览仅显示文字指导语；点击“进入面部取景”直接进入人脸准备阶段，
            // 不再经过“继续”这一层中间状态。
            viewModel.BeginFaceCheck();
            await StartCameraPreviewAsync();
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
        if (ViewModel is { } viewModel)
        {
            await StartCalibrationButtonAsync(viewModel);
        }
    }

    private async Task StartCalibrationButtonAsync(AssessmentCaptureViewModel viewModel)
    {
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

        if (!HasRecentCameraAnalysisFrame())
        {
            if (!viewModel.IsCameraOpen)
            {
                await StartCameraPreviewAsync();
            }

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
            if (viewModel.IsEmotionStroopModule)
            {
                isStartingFormalPresentation = true;
                try
                {
                    await EnterFormalPresentationModeAsync();
                    viewModel.StartCurrentModule();
                }
                finally
                {
                    // Stroop 分支会在这里直接返回，必须与通用启动路径一样复位标志。
                    // 否则人脸检测中断切回 FaceCheck 时会被误判为“仍在进入全屏”，
                    // 从而跳过 ExitFormalPresentationMode，导致侧边栏永久不可用。
                    isStartingFormalPresentation = false;
                    UpdateFormalPresentationForCurrentStage();
                }
                return;
            }

            if (viewModel.IsVoiceBaselineModule || viewModel.IsShortTextReadingModule)
            {
                var sessionName = await viewModel.GetOrStartUnifiedSessionKeyAsync();
                if (!viewModel.IsDevelopmentModuleOverride)
                {
                    await viewModel.BeginCurrentModuleAttemptAsync(sessionName);
                }
            }
            else
            {
                await BeginModuleRecordingSessionAsync(viewModel);
            }
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

        try
        {
            isStartingFormalPresentation = true;
            await EnterFormalPresentationModeAsync();
            viewModel.StartCurrentModule();
        }
        catch (Exception exception)
        {
            ExitFormalPresentationMode();
            StopModuleRecording(viewModel, CaptureMediaStopReason.Failed, exception.Message);
            await viewModel.FailCurrentModuleAttemptAsync("PRESENTATION_START_FAILED", exception.Message);
            viewModel.ShowStageNotice($"正式采集画面启动失败：{exception.Message}");
        }
        finally
        {
            isStartingFormalPresentation = false;
            UpdateFormalPresentationForCurrentStage();
        }
    }

    private async void StartSyncTestButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel)
        {
            await StartSyncTestAsync(viewModel);
        }
    }

    private async void StartEmotionStroopFormalButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel)
        {
            await StartEmotionStroopFormalButtonAsync(viewModel);
        }
    }

    private async Task StartEmotionStroopFormalButtonAsync(AssessmentCaptureViewModel viewModel)
    {
        try
        {
            await BeginModuleRecordingSessionAsync(viewModel);
            viewModel.StartEmotionStroopFormal();
        }
        catch (AssessmentModuleStartException exception)
        {
            viewModel.ShowStageNotice($"当前评估模块无法启动：{exception.InnerException?.Message ?? exception.Message}");
        }
        catch (Exception exception)
        {
            StopModuleRecording(viewModel, CaptureMediaStopReason.Failed, exception.Message);
            await viewModel.FailCurrentModuleAttemptAsync("MEDIA_START_FAILED", exception.Message);
            viewModel.ShowStageNotice($"正式采集启动失败：{exception.Message}");
        }
    }

    private async Task StartSyncTestAsync(AssessmentCaptureViewModel viewModel)
    {
        playbackTimer.Stop();
        DemoMedia.Stop();
        VideoBrowseMedia.Stop();

        if (!viewModel.HasSelectedCamera)
        {
            viewModel.ShowStageNotice(viewModel.Localize("CaptureWorkspaceNoCameraStageNotice"));
            return;
        }

        if (!HasRecentCameraAnalysisFrame())
        {
            if (!viewModel.IsCameraOpen)
            {
                await StartCameraPreviewAsync();
            }

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

    private async void StartVoiceBaselineButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel)
        {
            await StartVoiceBaselineButtonAsync(viewModel);
        }
    }

    private static async Task StartVoiceBaselineButtonAsync(AssessmentCaptureViewModel viewModel)
    {
        try
        {
            await viewModel.StartVoiceBaselineFirstSegmentAsync();
        }
        catch (Exception exception)
        {
            viewModel.ShowStageNotice($"语音基线录制启动失败：{exception.Message}");
        }
    }

    private void StartWordReadingButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel?.StartWordReadingFirstGroup();
    }

    private void FinishVoiceBaselineButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel?.FinishVoiceBaselineSegment();
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
            faceStatusTimer.Stop();
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
            faceStatusTimer.Start();
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
        faceStatusTimer.Stop();
        StopRecordingForPreviewStop();
        if (ViewModel is { } viewModel)
        {
            await viewModel.CloseCameraAsync();
        }

        ResetCameraPreviewDisplay();
    }

    private void ResetCameraPreviewDisplay()
    {
        ClearCameraPreviewVisual();
        (DataContext as AssessmentCaptureViewModel)?.ResetFaceReadiness();
        (DataContext as AssessmentCaptureViewModel)?.ResetFaceConditionMonitoring();
        lastCameraFaceStatusCapturedAt = DateTimeOffset.MinValue;
        lastRecordingStatusUpdateAt = DateTime.MinValue;
    }

    private void ClearCameraPreviewVisual()
    {
        CameraPreviewImage.Source = null;
        cameraPreviewBitmap = null;
        CameraGuideRectangle.Visibility = Visibility.Collapsed;
        CameraFaceRectangle.Visibility = Visibility.Collapsed;
        cameraPreviewHasFrame = false;
        latestCameraOverlay = null;
        lastCameraPreviewCapturedAt = DateTimeOffset.MinValue;
    }

    private void SetCameraPreviewRenderingEnabled(bool enabled)
    {
        var viewModel = ViewModel;
        viewModel?.SetCameraPreviewRenderingEnabled(enabled);
        if (enabled && viewModel?.IsCameraOpen == true)
        {
            cameraTimer.Start();
            return;
        }

        cameraTimer.Stop();
        ClearCameraPreviewVisual();
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
                CameraFaceRectangle.Visibility = Visibility.Collapsed;
                CameraPreviewStatusText.Text = viewModel.Localize("CaptureWorkspaceNoFrameRead");
            }

            return;
        }

        using (snapshot)
        {
            if (DateTimeOffset.Now - snapshot.CapturedAt > TimeSpan.FromSeconds(1))
            {
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
            if (viewModel.IsMediaRecording
                && DateTime.Now - lastRecordingStatusUpdateAt >= TimeSpan.FromMilliseconds(500))
            {
                viewModel.UpdateRecordedFrameCount(snapshot.RecordedFrameCount);
                lastRecordingStatusUpdateAt = DateTime.Now;
            }
        }
    }

    private void UpdateCameraFaceStatus()
    {
        var viewModel = ViewModel;
        if (viewModel is null || !viewModel.IsCameraOpen)
        {
            return;
        }

        CameraFaceStatusSnapshot status;
        if (!viewModel.TryTakeLatestCameraFaceStatus(out status))
        {
            if (lastCameraFaceStatusCapturedAt != DateTimeOffset.MinValue
                && DateTimeOffset.Now - lastCameraFaceStatusCapturedAt <= TimeSpan.FromSeconds(1))
            {
                return;
            }

            status = new CameraFaceStatusSnapshot(
                0,
                DateTimeOffset.Now,
                Stopwatch.GetTimestamp(),
                CameraFaceState.DetectorUnavailable,
                false);
        }
        else
        {
            lastCameraFaceStatusCapturedAt = status.CapturedAt;
        }

        viewModel.ObserveFaceReadiness(
            status.State,
            status.IsPrimaryFaceInsideGuide,
            status.AnalyzedAtTimestamp);
        viewModel.ObservePictureBrowseRestFace(
            status.State,
            status.IsPrimaryFaceInsideGuide,
            status.CapturedAt);
        var monitorUpdate = viewModel.ObserveFaceCondition(status.State, status.AnalyzedAtTimestamp);
        var faceStatusText = FaceStateText(viewModel, status.State);
        if (status.State == CameraFaceState.Normal && !status.IsPrimaryFaceInsideGuide)
        {
            faceStatusText = viewModel.Localize("CaptureWorkspaceMoveFaceIntoFrame");
        }

        if (viewModel.IsExecutingCaptureTask
            && viewModel.IsMediaRecording
            && !viewModel.IsSyncTestModule
            && !monitorUpdate.IsNormal)
        {
            faceStatusText = viewModel.Localize(
                "CaptureWorkspaceFaceAbnormalCountdown",
                faceStatusText,
                monitorUpdate.AbnormalDuration.TotalSeconds);
        }

        CameraPreviewStatusText.Text = faceStatusText;
    }

    private bool HasRecentCameraAnalysisFrame()
    {
        return lastCameraFaceStatusCapturedAt != DateTimeOffset.MinValue
            && DateTimeOffset.Now - lastCameraFaceStatusCapturedAt <= TimeSpan.FromSeconds(1);
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
