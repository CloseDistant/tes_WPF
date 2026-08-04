using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;
using System.ComponentModel;
using System.IO.Compression;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RuinaoSoftwareWpf.Views;

public partial class FemSimulationView : UserControl
{
    private const string ViewerFileName = "fem-result-viewer.html";
    private const string OfficialExampleViewerFileName = "fem-original-83y04.html";
    private bool viewerInitializationStarted;
    private bool viewerHandlersAttached;
    private bool viewerReady;
    private bool scalpVisible = true;
    private bool targetCoverageMode;
    private bool p95ComparisonMode;
    private string coverageViewMode = "solid";
    private string coverageBandMode = "all";
    private ScrollViewer? slicePanViewer;
    private Point slicePanOrigin;
    private double slicePanHorizontalOffset;
    private double slicePanVerticalOffset;
    private CancellationTokenSource? viewerReadyTimeoutCts;
    private CancellationTokenSource? sampleLoadCts;
    private FemSimulationViewModel? subscribedViewModel;
    private readonly SemaphoreSlim viewerNavigationLock = new(1, 1);
    private static readonly SemaphoreSlim ViewerDataPreparationLock = new(1, 1);

    public FemSimulationView()
    {
        InitializeComponent();
        DataContextChanged += ViewDataContextChanged;
        Unloaded += (_, _) =>
        {
            sampleLoadCts?.Cancel();
            sampleLoadCts?.Dispose();
            sampleLoadCts = null;
            CancelViewerReadyTimeout();
            if (subscribedViewModel is not null)
                subscribedViewModel.PropertyChanged -= ViewModelPropertyChanged;
            subscribedViewModel = null;
        };
    }

    private async void ViewLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is not FemSimulationViewModel viewModel)
            return;

        SubscribeToViewModel(viewModel);
        var cancellation = new CancellationTokenSource();
        sampleLoadCts = cancellation;
        try
        {
            if (await viewModel.EnsureBundledSampleLoadedAsync(cancellation.Token))
            {
                DebugLog.WriteInfo(
                    $"内置有限元示例已自动加载：{viewModel.ResultManifestPath}");
            }
            else
            {
                DebugLog.WriteWarning("未自动加载内置有限元示例，可使用“选择清单”手动导入。");
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // The page was closed while its bundled sample was loading.
        }
        catch (Exception exception)
        {
            DebugLog.WriteError($"内置有限元示例自动加载失败：{exception}");
        }
        finally
        {
            if (ReferenceEquals(sampleLoadCts, cancellation))
                sampleLoadCts = null;
            cancellation.Dispose();
        }
    }

    private async void ViewDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsLoaded && e.NewValue is FemSimulationViewModel viewModel)
        {
            SubscribeToViewModel(viewModel);
            if (viewModel.Is3D)
                await NavigateCurrentResultAsync();
        }
    }

    private void SubscribeToViewModel(FemSimulationViewModel viewModel)
    {
        if (ReferenceEquals(subscribedViewModel, viewModel)) return;
        if (subscribedViewModel is not null)
            subscribedViewModel.PropertyChanged -= ViewModelPropertyChanged;
        subscribedViewModel = viewModel;
        subscribedViewModel.PropertyChanged += ViewModelPropertyChanged;
    }

    private async void ViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not nameof(FemSimulationViewModel.Is3D) ||
            sender is not FemSimulationViewModel viewModel)
            return;

        try
        {
            if (viewModel.Is3D)
                await NavigateCurrentResultAsync();
            else
                ReleaseThreeDimensionalViewer();
        }
        catch (Exception exception)
        {
            ShowFemViewerError(exception.Message);
        }
    }

    private async Task InitializeFemViewerAsync()
    {
        if (viewerInitializationStarted) return;
        viewerInitializationStarted = true;
        FemWebView.Visibility = System.Windows.Visibility.Collapsed;
        FemViewerError.Visibility = System.Windows.Visibility.Collapsed;
        FemViewerLoading.Visibility = System.Windows.Visibility.Visible;

        try
        {
            var viewerDirectory = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "FemViewer");
            var viewerPath = System.IO.Path.Combine(
                viewerDirectory,
                ViewerFileName);
            if (!System.IO.File.Exists(viewerPath))
                throw new System.IO.FileNotFoundException("未找到三维有限元查看器资源。", viewerPath);

            if (FemWebView.CoreWebView2 is null)
                await FemWebView.EnsureCoreWebView2Async();
            var core = FemWebView.CoreWebView2 ?? throw new InvalidOperationException("WebView2 初始化未完成。");
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            if (!viewerHandlersAttached)
            {
                core.WebMessageReceived += FemViewerMessageReceived;
                core.ProcessFailed += FemViewerProcessFailed;
                FemWebView.NavigationCompleted += FemViewerNavigationCompleted;
                viewerHandlersAttached = true;
            }

            FemViewerLoading.Visibility = System.Windows.Visibility.Collapsed;
            FemWebView.Visibility = System.Windows.Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            viewerInitializationStarted = false;
            ShowFemViewerError(ex.Message);
        }
    }

    private void FemViewerMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        var message = e.TryGetWebMessageAsString();
        if (string.Equals(message, "ready", StringComparison.Ordinal))
        {
            viewerReady = true;
            CancelViewerReadyTimeout();
            FemViewerLoading.Visibility = System.Windows.Visibility.Collapsed;
            FemViewerError.Visibility = System.Windows.Visibility.Collapsed;
            FemWebView.Visibility = System.Windows.Visibility.Visible;
            WpfThreeDimensionalControls.IsEnabled = true;
            DebugLog.WriteInfo("三维查看器加载完成。");
            return;
        }

        if (message.StartsWith("metrics:", StringComparison.Ordinal))
        {
            UpdateTargetMetrics(message[8..]);
            return;
        }

        if (message.StartsWith("view:", StringComparison.Ordinal))
        {
            SetMedicalViewStatus(message[5..]);
            return;
        }

        if (message.StartsWith("clip-side:", StringComparison.Ordinal))
        {
            var side = message[10..];
            WpfCoverageLeft.IsChecked = string.Equals(side, "left", StringComparison.Ordinal);
            WpfCoverageRight.IsChecked = string.Equals(side, "right", StringComparison.Ordinal);
            UpdateWpfLayerStatus();
            return;
        }

        if (message.StartsWith("error:", StringComparison.Ordinal))
            ShowFemViewerError(message[6..]);
    }

    private void FemViewerNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!e.IsSuccess)
            ShowFemViewerError($"页面加载失败：{e.WebErrorStatus}");
    }

    private void FemViewerProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
    {
        var details =
            $"{e.ProcessFailedKind}；原因：{e.Reason}；退出码：{e.ExitCode}";

        if (e.ProcessFailedKind is CoreWebView2ProcessFailedKind.GpuProcessExited
            or CoreWebView2ProcessFailedKind.UtilityProcessExited
            or CoreWebView2ProcessFailedKind.RenderProcessUnresponsive)
        {
            // WebView2 can restart these helper processes or fall back to software rendering.
            // Treating them as fatal hides a page that may still complete normally.
            DebugLog.WriteWarning($"三维查看器辅助进程异常，继续等待自动恢复：{details}");
            return;
        }

        Dispatcher.Invoke(() => ShowFemViewerError($"三维渲染进程异常：{details}"));
    }

    private void ShowFemViewerError(string message)
    {
        viewerReady = false;
        WpfThreeDimensionalControls.IsEnabled = false;
        CancelViewerReadyTimeout();
        FemWebView.Visibility = System.Windows.Visibility.Collapsed;
        FemViewerLoading.Visibility = System.Windows.Visibility.Collapsed;
        FemViewerErrorMessage.Text = message;
        FemViewerError.Visibility = System.Windows.Visibility.Visible;
        DebugLog.WriteError($"三维查看器加载失败：{message}");
    }

    private async void ReloadFemViewerClick(object sender, System.Windows.RoutedEventArgs e)
    {
        FemViewerError.Visibility = System.Windows.Visibility.Collapsed;
        FemViewerLoading.Visibility = System.Windows.Visibility.Visible;
        viewerReady = false;
        if (FemWebView.CoreWebView2 is null)
        {
            viewerInitializationStarted = false;
            await InitializeFemViewerAsync();
            return;
        }

        try
        {
            await NavigateCurrentResultAsync();
        }
        catch (Exception ex)
        {
            ShowFemViewerError(ex.Message);
        }
    }

    private async Task NavigateCurrentResultAsync()
    {
        await viewerNavigationLock.WaitAsync();
        try
        {
            if (DataContext is not FemSimulationViewModel viewModel ||
                !viewModel.Is3D ||
                !viewModel.HasCompatible3DResult ||
                string.IsNullOrWhiteSpace(viewModel.ThreeDimensionalDataPath))
                return;

            if (FemWebView.CoreWebView2 is null)
            {
                await InitializeFemViewerAsync();
                if (FemWebView.CoreWebView2 is null) return;
            }

            var core = FemWebView.CoreWebView2;
            var viewerDirectory = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "FemViewer");
            viewerReady = false;
            ResetWpfViewerControls();
            var selectedViewerFileName =
                string.Equals(
                    viewModel.ThreeDimensionalViewerMode,
                    "official-static-83y04",
                    StringComparison.Ordinal)
                    ? OfficialExampleViewerFileName
                    : ViewerFileName;
            var viewerPath = System.IO.Path.Combine(
                viewerDirectory,
                selectedViewerFileName);
            if (!System.IO.File.Exists(viewerPath))
                throw new System.IO.FileNotFoundException("未找到三维有限元查看器资源。", viewerPath);

            var newestAssetTicks = System.IO.Directory
                .EnumerateFiles(viewerDirectory, "*", System.IO.SearchOption.AllDirectories)
                .Select(System.IO.File.GetLastWriteTimeUtc)
                .Max()
                .Ticks;
            var resultDataPath = await PrepareViewerDataAsync(
                viewModel.ThreeDimensionalDataPath,
                viewModel.SubjectId);
            if (!viewModel.Is3D) return;

            var resultDirectory = System.IO.Path.GetDirectoryName(resultDataPath)
                ?? throw new InvalidOperationException("无法确定三维结果缓存目录。");
            var resultTicks = System.IO.File.GetLastWriteTimeUtc(resultDataPath).Ticks;
            var versionedHostName = $"fem-{newestAssetTicks:x}.local";
            var resultHostName = $"fem-result-{resultTicks:x}.local";
            core.SetVirtualHostNameToFolderMapping(
                versionedHostName,
                viewerDirectory,
                CoreWebView2HostResourceAccessKind.DenyCors);
            core.SetVirtualHostNameToFolderMapping(
                resultHostName,
                resultDirectory,
                CoreWebView2HostResourceAccessKind.Allow);

            FemWebView.Visibility = System.Windows.Visibility.Collapsed;
            FemViewerError.Visibility = System.Windows.Visibility.Collapsed;
            FemViewerLoading.Visibility = System.Windows.Visibility.Visible;
            StartViewerReadyTimeout();
            var resultUrl = $"https://{resultHostName}/{Uri.EscapeDataString(System.IO.Path.GetFileName(resultDataPath))}";
            FemWebView.Source = new Uri(
                $"https://{versionedHostName}/{selectedViewerFileName}?host=wpf" +
                $"&data={Uri.EscapeDataString(resultUrl)}" +
                $"&load={DateTime.UtcNow.Ticks:x}");
            DebugLog.WriteInfo(
                $"三维查看器模式：{viewModel.ThreeDimensionalViewerMode}；资源：{selectedViewerFileName}");
        }
        finally
        {
            viewerNavigationLock.Release();
        }
    }

    private void ReleaseThreeDimensionalViewer()
    {
        viewerReady = false;
        CancelViewerReadyTimeout();
        WpfThreeDimensionalControls.IsEnabled = false;
        FemWebView.Visibility = System.Windows.Visibility.Collapsed;
        FemViewerLoading.Visibility = System.Windows.Visibility.Collapsed;
        FemViewerError.Visibility = System.Windows.Visibility.Collapsed;

        // The dynamic viewer has a render loop. Navigating away when the user
        // returns to 2D immediately releases its meshes, JSON and GPU context.
        if (FemWebView.CoreWebView2 is not null)
            FemWebView.CoreWebView2.Navigate("about:blank");
    }

    private static async Task<string> PrepareViewerDataAsync(
        string gzipPath,
        string subjectId)
    {
        await ViewerDataPreparationLock.WaitAsync();
        try
        {
            var safeSubject = string.Concat(subjectId.Select(character =>
                System.IO.Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
            var sourceInfo = new System.IO.FileInfo(gzipPath);
            var cacheDirectory = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RuinaoSoftwareWpf",
                "FemViewerCache",
                safeSubject,
                $"{sourceInfo.Length:x}-{sourceInfo.LastWriteTimeUtc.Ticks:x}");
            System.IO.Directory.CreateDirectory(cacheDirectory);
            var outputPath = System.IO.Path.Combine(cacheDirectory, "fem-3d-data.json");
            if (IsUsableViewerCache(outputPath)) return outputPath;

            var temporaryPath = System.IO.Path.Combine(
                cacheDirectory,
                $"fem-3d-data.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp");
            try
            {
                await using (var source = System.IO.File.OpenRead(gzipPath))
                await using (var gzip = new GZipStream(source, CompressionMode.Decompress))
                await using (var destination = new System.IO.FileStream(
                    temporaryPath,
                    System.IO.FileMode.CreateNew,
                    System.IO.FileAccess.Write,
                    System.IO.FileShare.None,
                    1024 * 1024,
                    useAsync: true))
                {
                    await gzip.CopyToAsync(destination);
                    await destination.FlushAsync();
                }

                try
                {
                    System.IO.File.Move(temporaryPath, outputPath, false);
                }
                catch (System.IO.IOException) when (IsUsableViewerCache(outputPath))
                {
                    // Another process completed the same immutable cache first.
                    System.IO.File.Delete(temporaryPath);
                }

                return outputPath;
            }
            finally
            {
                if (System.IO.File.Exists(temporaryPath))
                    System.IO.File.Delete(temporaryPath);
            }
        }
        finally
        {
            ViewerDataPreparationLock.Release();
        }
    }

    private static bool IsUsableViewerCache(string path) =>
        System.IO.File.Exists(path) && new System.IO.FileInfo(path).Length > 2;

    private void ResetWpfViewerControls()
    {
        WpfThreeDimensionalControls.IsEnabled = false;
        WpfFemFocusControls.IsEnabled = false;
        WpfFemFocusControls.Visibility = System.Windows.Visibility.Collapsed;
        WpfHeatThresholdPanel.Visibility = System.Windows.Visibility.Visible;
        WpfHeatThreshold.IsEnabled = true;
        WpfFocusStimulusButton.IsEnabled = true;
        WpfFocusStimulusButton.Visibility = System.Windows.Visibility.Visible;
        WpfRestoreFullModelButton.Visibility = System.Windows.Visibility.Collapsed;
        WpfToggleScalpButton.IsEnabled = true;
        WpfToggleScalpButton.Visibility = System.Windows.Visibility.Visible;
        WpfFocusStimulusButton.Content = "聚焦杏仁核刺激区";
        SetMedicalViewStatus("未聚焦");
        WpfLayerStatus.Text = "脑区 6/6 · 刺激层 2/2";
        SetCoverageModeUi(false);
        WpfCoverageClipDetails.Visibility = Visibility.Collapsed;
        scalpVisible = true;
        WpfToggleScalpButton.Content = "隐藏头部轮廓";
    }

    private async Task<bool> ExecuteViewerScriptAsync(string script)
    {
        if (!viewerReady || FemWebView.CoreWebView2 is null) return false;
        try
        {
            await FemWebView.CoreWebView2.ExecuteScriptAsync(script);
            return true;
        }
        catch (Exception ex)
        {
            ShowFemViewerError($"三维控制执行失败：{ex.Message}");
            return false;
        }
    }

    private Task<bool> ClickViewerElementAsync(string id) => ExecuteViewerScriptAsync(
        $"document.getElementById({JsonSerializer.Serialize(id)})?.click();");

    private Task<bool> SetViewerCheckboxAsync(string id, bool isChecked) => ExecuteViewerScriptAsync(
        $"(()=>{{const e=document.getElementById({JsonSerializer.Serialize(id)});if(e){{e.checked={isChecked.ToString().ToLowerInvariant()};e.dispatchEvent(new Event('change',{{bubbles:true}}));}}}})();");

    private void SlicePreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer viewer) return;
        var (transform, _) = GetSliceZoomParts(viewer);
        var nextScale = Math.Clamp(transform.ScaleX + (e.Delta > 0 ? 0.25 : -0.25), 1, 6);
        SetSliceZoom(viewer, nextScale, e.GetPosition(viewer));
        e.Handled = true;
    }

    private void SliceZoomClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag }) return;
        var parts = tag.Split(':', 2);
        if (parts.Length != 2) return;
        var viewer = parts[0] switch
        {
            "Sagittal" => SagittalScrollViewer,
            "Coronal" => CoronalScrollViewer,
            "Axial" => AxialScrollViewer,
            _ => null
        };
        if (viewer is null) return;
        var (transform, _) = GetSliceZoomParts(viewer);
        var nextScale = parts[1] switch
        {
            "In" => Math.Min(6, transform.ScaleX + 0.25),
            "Out" => Math.Max(1, transform.ScaleX - 0.25),
            _ => 1
        };
        SetSliceZoom(viewer, nextScale, new Point(viewer.ViewportWidth / 2, viewer.ViewportHeight / 2));
    }

    private void SetSliceZoom(ScrollViewer viewer, double nextScale, Point anchor)
    {
        var (transform, label) = GetSliceZoomParts(viewer);
        var previousScale = transform.ScaleX;
        if (Math.Abs(previousScale - nextScale) < 0.001) return;
        var contentX = viewer.HorizontalOffset + anchor.X;
        var contentY = viewer.VerticalOffset + anchor.Y;
        transform.ScaleX = nextScale;
        transform.ScaleY = nextScale;
        label.Text = $"{nextScale * 100:0}%";
        viewer.UpdateLayout();
        if (nextScale <= 1.001)
        {
            viewer.ScrollToHorizontalOffset(0);
            viewer.ScrollToVerticalOffset(0);
            return;
        }

        var ratio = nextScale / previousScale;
        viewer.ScrollToHorizontalOffset(contentX * ratio - anchor.X);
        viewer.ScrollToVerticalOffset(contentY * ratio - anchor.Y);
    }

    private (ScaleTransform Transform, TextBlock Label) GetSliceZoomParts(ScrollViewer viewer) =>
        ReferenceEquals(viewer, SagittalScrollViewer) ? (SagittalScaleTransform, SagittalZoomLabel) :
        ReferenceEquals(viewer, CoronalScrollViewer) ? (CoronalScaleTransform, CoronalZoomLabel) :
        (AxialScaleTransform, AxialZoomLabel);

    private void SlicePanStart(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ScrollViewer viewer) return;
        if (e.ClickCount == 2)
        {
            SetSliceZoom(viewer, 1, e.GetPosition(viewer));
            e.Handled = true;
            return;
        }

        var (transform, _) = GetSliceZoomParts(viewer);
        if (transform.ScaleX <= 1.001) return;
        slicePanViewer = viewer;
        slicePanOrigin = e.GetPosition(viewer);
        slicePanHorizontalOffset = viewer.HorizontalOffset;
        slicePanVerticalOffset = viewer.VerticalOffset;
        viewer.Cursor = Cursors.SizeAll;
        viewer.CaptureMouse();
        e.Handled = true;
    }

    private void SlicePanMove(object sender, MouseEventArgs e)
    {
        if (slicePanViewer is null || !ReferenceEquals(sender, slicePanViewer)) return;
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            EndSlicePan();
            return;
        }

        var current = e.GetPosition(slicePanViewer);
        slicePanViewer.ScrollToHorizontalOffset(slicePanHorizontalOffset - (current.X - slicePanOrigin.X));
        slicePanViewer.ScrollToVerticalOffset(slicePanVerticalOffset - (current.Y - slicePanOrigin.Y));
        e.Handled = true;
    }

    private void SlicePanEnd(object sender, MouseEventArgs e)
    {
        if (slicePanViewer is null || !ReferenceEquals(sender, slicePanViewer)) return;
        EndSlicePan();
        e.Handled = true;
    }

    private void EndSlicePan()
    {
        if (slicePanViewer is null) return;
        slicePanViewer.ReleaseMouseCapture();
        slicePanViewer.ClearValue(CursorProperty);
        slicePanViewer = null;
    }

    private async void FocusStimulusClick(object sender, System.Windows.RoutedEventArgs e)
    {
        if (!await ClickViewerElementAsync("focus-stimulus")) return;
        if (!await ClickViewerElementAsync("show-p95-comparison")) return;
        ApplyDefaultFocusUi();
    }

    private void ApplyDefaultFocusUi()
    {
        WpfFemFocusControls.IsEnabled = viewerReady;
        WpfFemFocusControls.Visibility = System.Windows.Visibility.Visible;
        WpfHeatThresholdPanel.Visibility = System.Windows.Visibility.Collapsed;
        WpfHeatThreshold.IsEnabled = false;
        WpfFocusStimulusButton.IsEnabled = false;
        WpfFocusStimulusButton.Visibility = System.Windows.Visibility.Collapsed;
        WpfRestoreFullModelButton.IsEnabled = true;
        WpfRestoreFullModelButton.Visibility = System.Windows.Visibility.Visible;
        WpfToggleScalpButton.IsEnabled = false;
        WpfToggleScalpButton.Visibility = System.Windows.Visibility.Collapsed;
        WpfFocusStimulusButton.Content = "已聚焦杏仁核刺激区";
        WpfShowFieldOuter.IsChecked = true;
        WpfShowFieldCore.IsChecked = true;
        WpfShowStimField.IsChecked = true;
        WpfShowFieldContours.IsChecked = false;
        WpfCoverageLegendHelp.Visibility = Visibility.Visible;
        WpfCoverageSideOptions.Visibility = Visibility.Visible;
        WpfCoverageClipExpander.Visibility = Visibility.Visible;
        WpfShowWhiteMatter.IsChecked = false;
        WpfCoverageLeft.IsChecked = true;
        WpfCoverageRight.IsChecked = true;
        WpfCoverageClipEnabled.IsChecked = false;
        WpfCoverageClipDetails.Visibility = Visibility.Collapsed;
        WpfCoverageClipPosition.Value = 50;
        SetCoverageClipAxisUi("coronal");
        WpfContextOpacity.Value = 6;
        WpfStructureOpacity.Value = 30;
        foreach (var checkBox in WpfStructurePicker.Children.OfType<CheckBox>()) checkBox.IsChecked = true;
        WpfSelectAllStructures.IsChecked = true;
        WpfStructureOpacity.IsEnabled = true;
        coverageViewMode = "solid";
        coverageBandMode = "all";
        SetCoverageModeUi(true);
        SetCoverageViewModeUi("solid");
        SetCoverageBandModeUi("all");
        SetMedicalViewStatus("冠状位 · 从前向后观察");
        UpdateWpfLayerStatus();
    }

    private async void RestoreFullModelClick(object sender, System.Windows.RoutedEventArgs e)
    {
        if (!await ClickViewerElementAsync("reset-camera")) return;
        WpfFemFocusControls.IsEnabled = false;
        WpfFemFocusControls.Visibility = System.Windows.Visibility.Collapsed;
        WpfHeatThresholdPanel.Visibility = System.Windows.Visibility.Visible;
        WpfHeatThreshold.IsEnabled = true;
        WpfFocusStimulusButton.IsEnabled = true;
        WpfFocusStimulusButton.Visibility = System.Windows.Visibility.Visible;
        WpfRestoreFullModelButton.Visibility = System.Windows.Visibility.Collapsed;
        WpfToggleScalpButton.IsEnabled = true;
        WpfToggleScalpButton.Visibility = System.Windows.Visibility.Visible;
        WpfFocusStimulusButton.Content = "聚焦杏仁核刺激区";
        SetMedicalViewStatus("未聚焦");
    }

    private async void ToggleScalpClick(object sender, System.Windows.RoutedEventArgs e)
    {
        if (!await ClickViewerElementAsync("toggle-scalp")) return;
        scalpVisible = !scalpVisible;
        WpfToggleScalpButton.Content = scalpVisible ? "隐藏头部轮廓" : "显示头部轮廓";
    }

    private async void ContextOpacityChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<double> e)
    {
        if (WpfContextOpacityLabel is null) return;
        var value = (int)Math.Round(e.NewValue);
        WpfContextOpacityLabel.Text = $"{value}%";
        if (!WpfFemFocusControls.IsEnabled) return;
        await ExecuteViewerScriptAsync($"(()=>{{const e=document.getElementById('context-opacity');e.value={value};e.dispatchEvent(new Event('input',{{bubbles:true}}));}})();");
    }

    private async void HeatThresholdChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<double> e)
    {
        if (WpfHeatThresholdLabel is null) return;
        var value = Math.Round(e.NewValue, 1);
        WpfHeatThresholdLabel.Text = $"{value:0.#}%";
        if (!viewerReady || !WpfHeatThreshold.IsEnabled) return;
        await ExecuteViewerScriptAsync($"(()=>{{const e=document.getElementById('heat-threshold');e.value={value.ToString(System.Globalization.CultureInfo.InvariantCulture)};e.dispatchEvent(new Event('input',{{bubbles:true}}));}})();");
    }

    private async void StructureOpacityChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<double> e)
    {
        if (WpfStructureOpacityLabel is null) return;
        var value = (int)Math.Round(e.NewValue);
        WpfStructureOpacityLabel.Text = $"{value}%";
        if (!WpfFemFocusControls.IsEnabled) return;
        await ExecuteViewerScriptAsync($"(()=>{{const e=document.getElementById('structure-opacity');e.value={value};e.dispatchEvent(new Event('input',{{bubbles:true}}));}})();");
    }

    private async void FieldLayerClick(object sender, System.Windows.RoutedEventArgs e)
    {
        if (sender is CheckBox { Tag: string id } checkBox)
        {
            await SetViewerCheckboxAsync(id, checkBox.IsChecked == true);
            if (id == "enable-coverage-clip")
            {
                WpfCoverageClipPosition.IsEnabled = checkBox.IsChecked == true;
                WpfCoverageClipDetails.Visibility = checkBox.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            }
            else if (id == "show-stim-field")
            {
                var showChildren = checkBox.IsChecked == true;
                WpfCoverageViewModeOptions.Visibility = showChildren ? Visibility.Visible : Visibility.Collapsed;
                WpfStimFieldDetails.Visibility = showChildren && coverageViewMode == "field" ? Visibility.Visible : Visibility.Collapsed;
                WpfCoverageBandOptions.Visibility = showChildren && coverageViewMode == "solid" ? Visibility.Visible : Visibility.Collapsed;
                WpfCoverageLegendHelp.Visibility = showChildren ? Visibility.Visible : Visibility.Collapsed;
                WpfCoverageSideOptions.Visibility = showChildren ? Visibility.Visible : Visibility.Collapsed;
                WpfCoverageClipExpander.Visibility = showChildren ? Visibility.Visible : Visibility.Collapsed;
                WpfShowFieldContours.IsEnabled = showChildren && coverageViewMode == "field";
                WpfCoverageClipDetails.Visibility = showChildren && WpfCoverageClipEnabled.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
                WpfCoverageClipPosition.IsEnabled = showChildren && WpfCoverageClipEnabled.IsChecked == true;
            }
            UpdateWpfLayerStatus();
        }
    }

    private async void CoverageModeClick(object sender, System.Windows.RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string id }) return;
        if (!await ClickViewerElementAsync(id)) return;
        SetCoverageModeUi(id == "show-p95-comparison");
    }

    private async void CoverageViewModeClick(object sender, System.Windows.RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string id }) return;
        if (!await ClickViewerElementAsync(id)) return;
        SetCoverageViewModeUi(id == "coverage-view-field" ? "field" : "solid");
    }

    private void SetCoverageViewModeUi(string mode)
    {
        coverageViewMode = mode == "field" ? "field" : "solid";
        SetSelectedTechButton(
            coverageViewMode == "field" ? WpfFieldDistributionButton : WpfSolidCoverageButton,
            WpfSolidCoverageButton,
            WpfFieldDistributionButton);

        var enabled = targetCoverageMode && WpfShowStimField.IsChecked == true;
        WpfCoverageViewModeOptions.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        WpfStimFieldDetails.Visibility = enabled && coverageViewMode == "field" ? Visibility.Visible : Visibility.Collapsed;
        WpfCoverageBandOptions.Visibility = enabled && coverageViewMode == "solid" ? Visibility.Visible : Visibility.Collapsed;
        WpfShowFieldContours.IsEnabled = enabled && coverageViewMode == "field";
        UpdateWpfLayerStatus();
    }

    private async void CoverageBandModeClick(object sender, System.Windows.RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string id }) return;
        if (!await ClickViewerElementAsync(id)) return;
        SetCoverageBandModeUi(id == "coverage-band-p95" ? "p95" : id == "coverage-band-uncovered" ? "uncovered" : "all");
    }

    private void SetCoverageBandModeUi(string mode)
    {
        coverageBandMode = mode == "p95" ? "p95" : mode == "uncovered" ? "uncovered" : "all";
        var selected = coverageBandMode == "p95"
            ? WpfCoverageBandP95Button
            : coverageBandMode == "uncovered" ? WpfCoverageBandUncoveredButton : WpfCoverageBandAllButton;
        SetSelectedTechButton(selected, WpfCoverageBandAllButton, WpfCoverageBandP95Button, WpfCoverageBandUncoveredButton);
        UpdateWpfLayerStatus();
    }

    private static void SetSelectedTechButton(Button selected, params Button[] buttons)
    {
        foreach (var button in buttons)
        {
            button.ClearValue(BackgroundProperty);
            button.ClearValue(BorderBrushProperty);
        }

        selected.Background = new SolidColorBrush(Color.FromRgb(0x12, 0x38, 0x45));
        selected.BorderBrush = new SolidColorBrush(Color.FromRgb(0x35, 0xD8, 0xE8));
    }

    private void SetCoverageModeUi(bool enabled)
    {
        targetCoverageMode = enabled;
        p95ComparisonMode = enabled;
        WpfFieldOverviewOptions.Visibility = enabled ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
        WpfAnatomyOptions.Visibility = System.Windows.Visibility.Visible;
        WpfCoverageOptions.Visibility = enabled ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        var analysisVisible = enabled && WpfShowStimField.IsChecked == true;
        WpfCoverageViewModeOptions.Visibility = analysisVisible ? Visibility.Visible : Visibility.Collapsed;
        WpfStimFieldDetails.Visibility = analysisVisible && coverageViewMode == "field" ? Visibility.Visible : Visibility.Collapsed;
        WpfCoverageBandOptions.Visibility = analysisVisible && coverageViewMode == "solid" ? Visibility.Visible : Visibility.Collapsed;
        WpfCoverageLegendHelp.Visibility = enabled && WpfShowStimField.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        WpfCoverageSideOptions.Visibility = enabled && WpfShowStimField.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        WpfCoverageClipExpander.Visibility = enabled && WpfShowStimField.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        WpfShowFieldContours.IsEnabled = analysisVisible && coverageViewMode == "field";
        WpfStructureOpacity.IsEnabled = HasVisibleReferenceStructureSelection();
        if (!enabled) WpfCoverageClipEnabled.IsChecked = false;
        WpfCoverageClipDetails.Visibility = enabled && WpfShowStimField.IsChecked == true && WpfCoverageClipEnabled.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        WpfCoverageClipPosition.IsEnabled = enabled && WpfShowStimField.IsChecked == true && WpfCoverageClipEnabled.IsChecked == true;
        var selected = enabled ? WpfP95ComparisonButton : WpfFieldOverviewButton;
        SetSelectedTechButton(selected, WpfFieldOverviewButton, WpfP95ComparisonButton);
        UpdateWpfLayerStatus();
    }

    private async void CoverageClipAxisClick(object sender, System.Windows.RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string id }) return;
        if (!await ClickViewerElementAsync(id)) return;
        SetCoverageClipAxisUi(id["clip-axis-".Length..]);
    }

    private void SetCoverageClipAxisUi(string axis)
    {
        foreach (var button in new[] { WpfClipSagittalButton, WpfClipCoronalButton, WpfClipAxialButton })
        {
            button.ClearValue(BackgroundProperty);
            button.ClearValue(BorderBrushProperty);
        }

        var selected = axis == "sagittal" ? WpfClipSagittalButton : axis == "axial" ? WpfClipAxialButton : WpfClipCoronalButton;
        selected.Background = new SolidColorBrush(Color.FromRgb(0x12, 0x38, 0x45));
        selected.BorderBrush = new SolidColorBrush(Color.FromRgb(0x35, 0xD8, 0xE8));
    }

    private async void CoverageClipPositionChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<double> e)
    {
        if (WpfCoverageClipPositionLabel is null) return;
        var value = (int)Math.Round(e.NewValue);
        WpfCoverageClipPositionLabel.Text = $"{value}%";
        if (!viewerReady || !WpfFemFocusControls.IsEnabled || !p95ComparisonMode || WpfShowStimField.IsChecked != true || WpfCoverageClipEnabled.IsChecked != true) return;
        await ExecuteViewerScriptAsync($"(()=>{{const e=document.getElementById('coverage-clip-position');e.value={value};e.dispatchEvent(new Event('input',{{bubbles:true}}));}})();");
    }

    private async void StructureCheckClick(object sender, System.Windows.RoutedEventArgs e)
    {
        if (sender is not CheckBox { Tag: string key } checkBox) return;
        var selector = $".structure-check-input[value={JsonSerializer.Serialize(key)}]";
        var isChecked = (checkBox.IsChecked == true).ToString().ToLowerInvariant();
        await ExecuteViewerScriptAsync($"(()=>{{const e=document.querySelector({JsonSerializer.Serialize(selector)});if(e){{e.checked={isChecked};e.dispatchEvent(new Event('change',{{bubbles:true}}));}}}})();");
        WpfStructureOpacity.IsEnabled = HasVisibleReferenceStructureSelection();
        UpdateWpfSelectAllState();
        UpdateWpfLayerStatus();
    }

    private async void SelectAllStructuresCheckClick(object sender, System.Windows.RoutedEventArgs e)
    {
        var selected = WpfSelectAllStructures.IsChecked == true;
        WpfSelectAllStructures.IsChecked = selected;
        foreach (var checkBox in WpfStructurePicker.Children.OfType<CheckBox>()) checkBox.IsChecked = selected;
        WpfStructureOpacity.IsEnabled = HasVisibleReferenceStructureSelection();
        var jsValue = selected.ToString().ToLowerInvariant();
        await ExecuteViewerScriptAsync($"document.getElementById('select-all-structures').checked={jsValue};document.getElementById('select-all-structures').dispatchEvent(new Event('change',{{bubbles:true}}));");
        UpdateWpfLayerStatus();
    }

    private async void SelectTargetOnlyClick(object sender, System.Windows.RoutedEventArgs e)
    {
        foreach (var checkBox in WpfStructurePicker.Children.OfType<CheckBox>()) checkBox.IsChecked = Equals(checkBox.Tag, "amygdala");
        WpfStructureOpacity.IsEnabled = HasVisibleReferenceStructureSelection();
        UpdateWpfSelectAllState();
        await ClickViewerElementAsync("select-target-only");
        UpdateWpfLayerStatus();
    }

    private async void FitVisibleStructuresClick(object sender, System.Windows.RoutedEventArgs e)
    {
        if (!await ClickViewerElementAsync("fit-visible-structures")) return;
        SetMedicalViewStatus("适配已选结构 · 可自由旋转");
    }

    private void UpdateWpfSelectAllState()
    {
        var selections = WpfStructurePicker.Children.OfType<CheckBox>().Select(item => item.IsChecked == true).ToArray();
        WpfSelectAllStructures.IsChecked = selections.All(value => value) ? true : selections.Any(value => value) ? null : false;
    }

    private bool HasVisibleReferenceStructureSelection() =>
        WpfStructurePicker.Children.OfType<CheckBox>().Any(item =>
            item.IsChecked == true && (!p95ComparisonMode || !Equals(item.Tag, "amygdala")));

    private void UpdateWpfLayerStatus()
    {
        if (targetCoverageMode)
        {
            var sideCount = (WpfCoverageLeft.IsChecked == true ? 1 : 0) + (WpfCoverageRight.IsChecked == true ? 1 : 0);
            var field = WpfShowStimField.IsChecked != true
                ? "仅目标轮廓"
                : coverageViewMode == "solid"
                    ? coverageBandMode == "p95" ? "实体覆盖 · 仅 P95"
                        : coverageBandMode == "uncovered" ? "实体覆盖 · 仅未覆盖" : "实体覆盖 · 全部分区"
                : WpfShowFieldContours.IsChecked == true
                    ? "目标邻域场 + 全局轮廓"
                    : "目标邻域场";
            var clip = WpfShowStimField.IsChecked == true && WpfCoverageClipEnabled.IsChecked == true ? " · 剖切已启用" : string.Empty;
            WpfLayerStatus.Text = $"目标表面覆盖：侧别 {sideCount}/2 · {field}{clip}";
            return;
        }

        var regionCount = WpfStructurePicker.Children.OfType<CheckBox>().Count(item => item.IsChecked == true);
        var fieldCount = (WpfShowFieldOuter.IsChecked == true ? 1 : 0) + (WpfShowFieldCore.IsChecked == true ? 1 : 0);
        var whiteMatter = WpfShowWhiteMatter.IsChecked == true ? " · 白质已显示" : string.Empty;
        WpfLayerStatus.Text = $"脑区 {regionCount}/6 · 刺激层 {fieldCount}/2{whiteMatter}";
    }

    private async void ResetFocusDisplayClick(object sender, System.Windows.RoutedEventArgs e)
    {
        if (!await ClickViewerElementAsync("focus-stimulus")) return;
        if (!await ClickViewerElementAsync("show-p95-comparison")) return;
        ApplyDefaultFocusUi();
    }

    private void ToggleFemControlPanelClick(object sender, System.Windows.RoutedEventArgs e)
    {
        var collapse = FemControlColumn.Width.Value > 0;
        FemControlColumn.Width = collapse ? new System.Windows.GridLength(0) : new System.Windows.GridLength(350);
        FemControlPanel.Visibility = collapse ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
        FemSidebarToggle.Content = collapse ? "›" : "‹";
        FemSidebarToggle.ToolTip = collapse ? "展开参数栏" : "收起参数栏";
    }

    private async void MedicalViewClick(object sender, System.Windows.RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string view }) return;
        var selector = $".view-preset[data-view={JsonSerializer.Serialize(view)}]";
        await ExecuteViewerScriptAsync($"document.querySelector({JsonSerializer.Serialize(selector)})?.click();");
        SetMedicalViewStatus(view switch
        {
            "sagittal" => "矢状位 · 从右向左观察",
            "axial" => "轴位 · 从上向下观察",
            _ => "冠状位 · 从前向后观察"
        });
    }

    private void SetMedicalViewStatus(string status)
    {
        WpfMedicalViewStatus.Text = status;
        var buttons = new[] { WpfSagittalViewButton, WpfCoronalViewButton, WpfAxialViewButton };
        foreach (var button in buttons)
        {
            button.ClearValue(BackgroundProperty);
            button.ClearValue(BorderBrushProperty);
        }

        Button? selected = status.StartsWith("矢状位", StringComparison.Ordinal) ? WpfSagittalViewButton
            : status.StartsWith("冠状位", StringComparison.Ordinal) ? WpfCoronalViewButton
            : status.StartsWith("轴位", StringComparison.Ordinal) ? WpfAxialViewButton
            : null;
        if (selected is null) return;
        selected.Background = new SolidColorBrush(Color.FromRgb(0x12, 0x38, 0x45));
        selected.BorderBrush = new SolidColorBrush(Color.FromRgb(0x35, 0xD8, 0xE8));
    }

    private void UpdateTargetMetrics(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var bilateral = root.GetProperty("bilateral");
            var left = root.GetProperty("left");
            var right = root.GetProperty("right");
            var p95Threshold = root.GetProperty("thresholds").GetProperty("p95").GetDouble();
            WpfTargetMetrics.Text =
                $"全脑 P95 阈值 {p95Threshold:F3} V/m\n" +
                $"双侧均值 {bilateral.GetProperty("mean").GetDouble():F3} V/m\n" +
                $"左 / 右均值 {left.GetProperty("mean").GetDouble():F3} / {right.GetProperty("mean").GetDouble():F3} V/m\n" +
                $"双侧 P95 覆盖 {bilateral.GetProperty("coverageP95").GetDouble():F1}%\n" +
                $"左 / 右 P95 覆盖 {left.GetProperty("coverageP95").GetDouble():F1}% / {right.GetProperty("coverageP95").GetDouble():F1}%\n" +
                $"双侧杏仁核体积 {bilateral.GetProperty("volumeMm3").GetDouble():F0} mm³；达到 P95 {bilateral.GetProperty("volumeP95Mm3").GetDouble():F0} mm³";

            SetCoverageBar(left, WpfLeftUncoveredColumn, WpfLeftP95Column);
            SetCoverageBar(right, WpfRightUncoveredColumn, WpfRightP95Column);
        }
        catch (Exception)
        {
            WpfTargetMetrics.Text = "刺激统计读取失败";
        }
    }

    private static void SetCoverageBar(JsonElement metrics, ColumnDefinition uncovered, ColumnDefinition p95)
    {
        var p95Coverage = metrics.GetProperty("coverageP95").GetDouble();
        uncovered.Width = new System.Windows.GridLength(100 - p95Coverage, System.Windows.GridUnitType.Star);
        p95.Width = new System.Windows.GridLength(p95Coverage, System.Windows.GridUnitType.Star);
    }

    private void StartViewerReadyTimeout()
    {
        CancelViewerReadyTimeout();
        var timeoutCts = new CancellationTokenSource();
        viewerReadyTimeoutCts = timeoutCts;
        _ = WaitForViewerReadyAsync(timeoutCts);
    }

    private void CancelViewerReadyTimeout()
    {
        var timeoutCts = viewerReadyTimeoutCts;
        viewerReadyTimeoutCts = null;
        if (timeoutCts is null)
            return;

        try
        {
            timeoutCts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // A completed timeout may be disposed while a reload is being requested.
        }
    }

    private async Task WaitForViewerReadyAsync(CancellationTokenSource timeoutCts)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(20), timeoutCts.Token);
            if (ReferenceEquals(viewerReadyTimeoutCts, timeoutCts))
            {
                viewerReadyTimeoutCts = null;
                ShowFemViewerError("三维查看器加载超时，请检查网页渲染组件或点击重新加载。");
            }
        }
        catch (OperationCanceledException)
        {
            // The viewer reported ready or a newer navigation replaced this one.
        }
        finally
        {
            if (ReferenceEquals(viewerReadyTimeoutCts, timeoutCts))
                viewerReadyTimeoutCts = null;
            timeoutCts.Dispose();
        }
    }

    private async void ChooseModelClick(object sender, System.Windows.RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "导入自研 CHARM/FEM 结果清单",
            Filter = "结果清单 (result-manifest.json)|result-manifest.json|JSON 文件 (*.json)|*.json",
            CheckFileExists = true
        };
        if (dialog.ShowDialog() == true && DataContext is FemSimulationViewModel viewModel)
        {
            try
            {
                await viewModel.LoadResultPackageAsync(dialog.FileName);
            }
            catch (Exception exception)
            {
                DebugLog.WriteError($"结果包加载失败：{exception}");
                MessageBox.Show(
                    exception.Message,
                    "结果包加载失败",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }

    private void ExportClick(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is not FemSimulationViewModel viewModel || !viewModel.HasModel) return;
        var dialog = new SaveFileDialog { Title = "导出有限元可视化配置", Filter = "有限元配置 (*.fem.json)|*.fem.json", FileName = "fem-visualization.fem.json" };
        if (dialog.ShowDialog() != true) return;
        System.IO.File.WriteAllText(dialog.FileName, System.Text.Json.JsonSerializer.Serialize(viewModel.CreateExportData(), new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        var directory = System.IO.Path.GetDirectoryName(dialog.FileName)!;
        var stem = System.IO.Path.GetFileNameWithoutExtension(System.IO.Path.GetFileNameWithoutExtension(dialog.FileName));
        SavePng(viewModel.CoronalImage, System.IO.Path.Combine(directory, stem + "-coronal.png"));
        SavePng(viewModel.SagittalImage, System.IO.Path.Combine(directory, stem + "-sagittal.png"));
        SavePng(viewModel.AxialImage, System.IO.Path.Combine(directory, stem + "-axial.png"));
        viewModel.MarkExported(dialog.FileName);
    }

    private static void SavePng(BitmapSource? bitmap, string path)
    {
        if (bitmap is null) return;
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = System.IO.File.Create(path);
        encoder.Save(stream);
    }

}
