using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using RuinaoHardwareDebugWpf.Views.Renderers;

namespace RuinaoHardwareDebugWpf.Views;

public partial class EegSignalCaptureView : UserControl
{
    private static readonly Lazy<IReadOnlyDictionary<string, EegElectrodeCoordinate>> ElectrodeCoordinates = new(LoadElectrodeCoordinates);

    private readonly List<ElectrodeVisual> electrodeVisuals = new();
    private EegSignalCaptureViewModel? viewModel;
    private EegWaveformRenderModel? currentModel;
    private int lastTimeAxisPageIndex = -1;
    private double lastTimeAxisWidth = -1;
    private double lastTimeAxisSurfaceLeft = double.NaN;

    public EegSignalCaptureView()
    {
        InitializeComponent();

        Loaded += (_, _) =>
        {
            Focus();
            AttachViewModel();
            InitializeMarkers();
            BuildElectrodeMap();
            LayoutWaveforms();
            RenderModel(viewModel?.GetCurrentRenderModel());
        };

        Unloaded += (_, _) =>
        {
            if (viewModel?.StopCommand.CanExecute(null) == true)
            {
                viewModel.StopCommand.Execute(null);
            }
            DetachViewModel();
        };
        DataContextChanged += (_, _) =>
        {
            DetachViewModel();
            AttachViewModel();
        };
    }

    private void AttachViewModel()
    {
        if (DataContext is not EegSignalCaptureViewModel nextViewModel || ReferenceEquals(viewModel, nextViewModel))
        {
            return;
        }

        viewModel = nextViewModel;
        viewModel.RenderModelUpdated += ViewModel_RenderModelUpdated;
        viewModel.MarkersChanged += ViewModel_MarkersChanged;
        RecordNameTextBox.Text = viewModel.RecordName;
        UpdateParameterSummary(viewModel.GetCurrentRenderModel().Config);
    }

    private void DetachViewModel()
    {
        if (viewModel is null)
        {
            return;
        }

        viewModel.RenderModelUpdated -= ViewModel_RenderModelUpdated;
        viewModel.MarkersChanged -= ViewModel_MarkersChanged;
        viewModel = null;
    }

    private void ViewModel_RenderModelUpdated(object? sender, EegWaveformRenderModel model)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => RenderModel(model));
            return;
        }

        RenderModel(model);
    }

    private void ViewModel_MarkersChanged(object? sender, IReadOnlyList<EegMarkerRecord> markers)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => RenderMarkerList(markers));
            return;
        }

        RenderMarkerList(markers);
    }

    private void InitializeMarkers()
    {
        if (viewModel is null)
        {
            return;
        }

        ShortcutPanel.Children.Clear();
        foreach (var tag in viewModel.MarkerTags)
        {
            var button = new Button
            {
                Content = $"{tag.KeyText} {tag.Name}",
                Background = new SolidColorBrush(tag.Color),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                FontWeight = FontWeights.SemiBold,
                Height = 24,
                Padding = new Thickness(10, 0, 10, 0),
                Margin = new Thickness(6, 0, 0, 0),
                Tag = tag
            };
            button.Click += (_, _) => AddMarker(tag, "manual");
            ShortcutPanel.Children.Add(button);
        }

        var addButton = new Button
        {
            Content = "+ 自定义标签",
            Height = 24,
            Padding = new Thickness(10, 0, 10, 0),
            Margin = new Thickness(8, 0, 0, 0),
            Background = new SolidColorBrush(Color.FromRgb(39, 44, 56)),
            Foreground = new SolidColorBrush(Color.FromRgb(228, 232, 239)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(48, 54, 69))
        };
        addButton.Click += CustomMarkerButton_Click;
        ShortcutPanel.Children.Add(addButton);
    }

    private void CustomMarkerButton_Click(object sender, RoutedEventArgs e)
    {
        if (viewModel is null)
        {
            return;
        }

        var dialog = new EegMarkerSettingsDialog(viewModel.MarkerTags)
        {
            Owner = Window.GetWindow(this)
        };

        if (dialog.ShowDialog() == true)
        {
            viewModel.ReplaceMarkerTags(dialog.MarkerTags);
            InitializeMarkers();
            FooterHintText.Text = "事件标记已更新：采集中可使用新快捷键打标。";
        }
    }

    private void LayoutWaveforms()
    {
        var channelCount = currentModel?.Config.ChannelCount ?? new EegAcquisitionConfig().ChannelCount;
        WaveformSurface.Height = EegWaveformRenderSurface.WaveTop + channelCount * EegWaveformRenderSurface.ChannelLaneHeight + EegWaveformRenderSurface.WaveBottom;
        var availableWidth = GetVisibleWaveformWidth();
        WaveformSurface.Width = availableWidth;
        TimeAxisCanvas.Width = WaveformSurface.Width;
        lastTimeAxisWidth = -1;
        DrawTimeAxis();
    }

    private void BuildElectrodeMap()
    {
        var config = currentModel?.Config ?? new EegAcquisitionConfig();
        ElectrodeCanvas.Children.Clear();
        electrodeVisuals.Clear();

        var width = Math.Max(240, ElectrodeCanvas.ActualWidth);
        var height = Math.Max(260, ElectrodeCanvas.ActualHeight);
        var dotSize = GetElectrodeDotSize(width, height);
        var dotRadius = dotSize / 2;
        var centerX = width / 2;
        var centerY = height * 0.51;
        var radius = Math.Max(1, Math.Min(width * 0.43, height * 0.40) - dotRadius);
        Point ToCanvasPoint(Point normalized) => new(centerX + normalized.X * radius, centerY - normalized.Y * radius);

        var head = new Ellipse
        {
            Width = radius * 2,
            Height = radius * 2,
            Stroke = new SolidColorBrush(Color.FromRgb(48, 54, 69)),
            StrokeThickness = 2,
            Fill = new SolidColorBrush(Color.FromRgb(28, 32, 43))
        };
        Canvas.SetLeft(head, centerX - radius);
        Canvas.SetTop(head, centerY - radius);
        ElectrodeCanvas.Children.Add(head);

        var contourBrush = new SolidColorBrush(Color.FromRgb(62, 68, 91));
        var nose = new Polygon
        {
            Points = new PointCollection
            {
                new(centerX - radius * 0.08, centerY - radius),
                new(centerX, centerY - radius * 1.12),
                new(centerX + radius * 0.08, centerY - radius)
            },
            Stroke = contourBrush,
            StrokeThickness = 1.4,
            Fill = Brushes.Transparent
        };
        ElectrodeCanvas.Children.Add(nose);

        foreach (var earX in new[] { centerX - radius * 1.04, centerX + radius * 1.04 })
        {
            var ear = new Ellipse
            {
                Width = radius * 0.18,
                Height = radius * 0.38,
                Stroke = contourBrush,
                StrokeThickness = 1.4,
                Fill = Brushes.Transparent
            };
            Canvas.SetLeft(ear, earX - ear.Width / 2);
            Canvas.SetTop(ear, centerY - ear.Height / 2);
            ElectrodeCanvas.Children.Add(ear);
        }

        for (var i = 0; i < config.ChannelCount; i++)
        {
            var name = config.ChannelNames[i];
            var canvasPoint = ToCanvasPoint(GetElectrode1020Position(name));
            var status = i % 11 == 0 ? ElectrodeStatus.Bad : i % 5 == 0 ? ElectrodeStatus.Warn : ElectrodeStatus.Good;
            var dot = CreateElectrodeDot(name, status, dotSize);
            Canvas.SetLeft(dot, canvasPoint.X - dotRadius);
            Canvas.SetTop(dot, canvasPoint.Y - dotRadius);
            ElectrodeCanvas.Children.Add(dot);
            electrodeVisuals.Add(new ElectrodeVisual(i, dot));
        }
    }

    private static double GetElectrodeDotSize(double width, double height)
    {
        return Math.Max(10, Math.Min(16, Math.Min(width, height) / 22));
    }

    private static Point GetElectrode1020Position(string name)
    {
        var lookupName = GetElectrodeCoordinateName(name);
        if (ElectrodeCoordinates.Value.TryGetValue(lookupName, out var coordinate))
        {
            return new Point(coordinate.XNorm, coordinate.YNorm);
        }

        return new Point(0, 0);
    }

    private static string GetElectrodeCoordinateName(string name)
    {
        return name;
    }

    private static string GetElectrodeDisplayName(string name)
    {
        return GetElectrodeCoordinateName(name);
    }

    private static IReadOnlyDictionary<string, EegElectrodeCoordinate> LoadElectrodeCoordinates()
    {
        var path = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Eeg", "EEG_64_standard_1020_coordinates.json");
        if (!File.Exists(path))
        {
            return new Dictionary<string, EegElectrodeCoordinate>(StringComparer.OrdinalIgnoreCase);
        }

        var json = File.ReadAllText(path);
        var coordinates = JsonSerializer.Deserialize<Dictionary<string, EegElectrodeCoordinate>>(json);
        return coordinates is null
            ? new Dictionary<string, EegElectrodeCoordinate>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, EegElectrodeCoordinate>(coordinates, StringComparer.OrdinalIgnoreCase);
    }

    private Border CreateElectrodeDot(string name, ElectrodeStatus status, double dotSize)
    {
        var background = status switch
        {
            ElectrodeStatus.Good => "#5DDA77",
            ElectrodeStatus.Warn => "#FFD84D",
            _ => "#E84E4F"
        };

        var dot = new Border
        {
            Width = dotSize,
            Height = dotSize,
            CornerRadius = new CornerRadius(dotSize / 2),
            Background = (Brush)new BrushConverter().ConvertFromString(background)!,
            ToolTip = $"{GetElectrodeDisplayName(name)} 阻抗：{(status == ElectrodeStatus.Good ? "<5kΩ" : status == ElectrodeStatus.Warn ? "5-10kΩ" : ">10kΩ")}",
            Child = new TextBlock
            {
                Text = GetElectrodeDisplayName(name),
                Foreground = Brushes.White,
                FontSize = Math.Max(5.5, dotSize * 0.38),
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        dot.MouseEnter += (_, _) => HighlightChannel(Array.IndexOf((currentModel?.Config.ChannelNames ?? EegDefaultChannels.Names).ToArray(), name));
        dot.MouseLeftButtonDown += (_, _) => HighlightChannel(Array.IndexOf((currentModel?.Config.ChannelNames ?? EegDefaultChannels.Names).ToArray(), name));
        return dot;
    }

    private void ParameterSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (viewModel is null)
        {
            return;
        }

        if (viewModel.IsRecording)
        {
            FooterHintText.Text = "采集中不能修改参数设置，请先结束采集。";
            MessageBox.Show("采集中不能修改参数设置，请先结束采集。", "参数设置", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new EegParameterSettingsDialog(viewModel.GetCurrentRenderModel().Config)
        {
            Owner = Window.GetWindow(this)
        };

        if (dialog.ShowDialog() == true)
        {
            viewModel.ApplyAcquisitionConfig(dialog.SelectedConfig);
            UpdateParameterSummary(dialog.SelectedConfig);
            LayoutWaveforms();
            RenderModel(viewModel.GetCurrentRenderModel());
        }
    }

    private void ExportDataButton_Click(object sender, RoutedEventArgs e)
    {
        var defaultName = string.IsNullOrWhiteSpace(RecordNameTextBox.Text)
            ? $"EEG_{DateTime.Now:yyyyMMdd_HHmmss}"
            : RecordNameTextBox.Text.Trim();
        var dialog = new EegExportDataDialog(defaultName)
        {
            Owner = Window.GetWindow(this)
        };

        if (dialog.ShowDialog() == true)
        {
            FooterHintText.Text = $"已选择 EDF 导出位置：{dialog.ExportFilePath}。EDF 编码逻辑后续接入。";
        }
    }

    private void RenderModel(EegWaveformRenderModel? model)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => RenderModel(model));
            return;
        }

        if (model is null)
        {
            return;
        }

        currentModel = model;
        RecordingTimeText.Text = model.Elapsed.ToString(@"hh\:mm\:ss");
        UpdateParameterSummary(model.Config);
        UpdateParameterSettingsAvailability();
        WaveformSurface.Model = model;
        DrawTimeAxis();
    }

    private void UpdateParameterSummary(EegAcquisitionConfig config)
    {
        SampleRateValueText.Text = $"{config.SampleRateHz}Hz";
        HighPassValueText.Text = FormatFilterValue(config.HighPassHz);
        LowPassValueText.Text = FormatFilterValue(config.LowPassHz);
        NotchValueText.Text = FormatFilterValue(config.NotchHz);
        GainValueText.Text = $"×{config.HardwareGain}";
    }

    private static string FormatFilterValue(double? value)
    {
        return value is null ? "OFF" : $"{FormatNumber(value.Value)} Hz";
    }

    private static string FormatNumber(double value)
    {
        return value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private void UpdateParameterSettingsAvailability()
    {
        if (ParameterSettingsButton is null)
        {
            return;
        }

        ParameterSettingsButton.IsEnabled = viewModel?.IsRecording != true;
    }

    private void DrawTimeAxis()
    {
        var model = currentModel ?? viewModel?.GetCurrentRenderModel();
        if (model is null)
        {
            return;
        }

        var axisWidth = GetWaveCanvasWidth();
        var surfaceLeft = GetWaveSurfaceLeftOnAxis();
        if (model.PageIndex == lastTimeAxisPageIndex
            && Math.Abs(axisWidth - lastTimeAxisWidth) < 0.5
            && !double.IsNaN(lastTimeAxisSurfaceLeft)
            && Math.Abs(surfaceLeft - lastTimeAxisSurfaceLeft) < 0.5)
        {
            return;
        }

        lastTimeAxisPageIndex = model.PageIndex;
        lastTimeAxisWidth = axisWidth;
        lastTimeAxisSurfaceLeft = surfaceLeft;
        TimeAxisCanvas.Children.Clear();
        var waveStart = surfaceLeft + EegWaveformRenderSurface.WaveLeft;
        var waveEnd = surfaceLeft + axisWidth - EegWaveformRenderSurface.WaveRightPadding;
        var waveWidth = Math.Max(1, waveEnd - waveStart);
        var axisY = 4.0;

        TimeAxisCanvas.Children.Add(new Line
        {
            X1 = waveStart,
            X2 = waveEnd,
            Y1 = axisY,
            Y2 = axisY,
            Stroke = new SolidColorBrush(Color.FromRgb(142, 150, 168)),
            StrokeThickness = 1,
            Opacity = 0.85
        });

        var pageStartSeconds = model.PageIndex * model.Config.PageSeconds;
        for (var seconds = 0; seconds <= model.Config.PageSeconds; seconds++)
        {
            var x = waveStart + seconds * waveWidth / model.Config.PageSeconds;
            var isMajorTick = seconds % 10 == 0;
            var showLabel = seconds % 5 == 0;
            TimeAxisCanvas.Children.Add(new Line
            {
                X1 = x,
                X2 = x,
                Y1 = axisY,
                Y2 = axisY + (isMajorTick ? 7 : 4),
                Stroke = new SolidColorBrush(Color.FromRgb(142, 150, 168)),
                StrokeThickness = 1,
                Opacity = isMajorTick ? 0.9 : 0.45
            });

            if (!showLabel)
            {
                continue;
            }

            var label = new TextBlock
            {
                Text = TimeSpan.FromSeconds(pageStartSeconds + seconds).ToString(@"hh\:mm\:ss"),
                Foreground = new SolidColorBrush(Color.FromRgb(142, 150, 168)),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 11
            };
            Canvas.SetLeft(label, GetTimeAxisLabelLeft(model, seconds, x, waveStart, waveEnd));
            Canvas.SetTop(label, axisY + 6);
            TimeAxisCanvas.Children.Add(label);
        }
    }

    private static double GetTimeAxisLabelLeft(EegWaveformRenderModel model, int seconds, double tickX, double waveStart, double waveEnd)
    {
        if (seconds == 0)
        {
            return waveStart;
        }

        if (seconds == model.Config.PageSeconds)
        {
            return Math.Max(waveStart, waveEnd - 56);
        }

        return Math.Min(
            Math.Max(waveStart, tickX - 28),
            Math.Max(waveStart, waveEnd - 56));
    }

    private double GetWaveSurfaceLeftOnAxis()
    {
        try
        {
            var point = WaveformSurface.TranslatePoint(new Point(0, 0), TimeAxisCanvas);
            if (!double.IsNaN(point.X) && !double.IsInfinity(point.X))
            {
                return point.X;
            }
        }
        catch (InvalidOperationException)
        {
            // Layout can briefly be unavailable during load/unload; fall back to zero.
        }

        return 0;
    }

    private double GetWaveCanvasWidth()
    {
        if (!double.IsNaN(WaveformSurface.ActualWidth) && WaveformSurface.ActualWidth > 1)
        {
            return WaveformSurface.ActualWidth;
        }

        if (!double.IsNaN(WaveformSurface.Width) && WaveformSurface.Width > 1)
        {
            return WaveformSurface.Width;
        }

        if (!double.IsNaN(TimeAxisCanvas.ActualWidth) && TimeAxisCanvas.ActualWidth > 1)
        {
            return TimeAxisCanvas.ActualWidth;
        }

        if (!double.IsNaN(TimeAxisCanvas.Width) && TimeAxisCanvas.Width > 1)
        {
            return TimeAxisCanvas.Width;
        }

        return GetVisibleWaveformWidth();
    }

    private double GetVisibleWaveformWidth()
    {
        var width = WaveformScrollViewer.ViewportWidth;
        if (double.IsNaN(width) || double.IsInfinity(width) || width <= 1)
        {
            width = WaveformScrollViewer.ActualWidth;
        }

        if (double.IsNaN(width) || double.IsInfinity(width) || width <= 1)
        {
            width = TimeAxisCanvas.ActualWidth;
        }

        if (double.IsNaN(width) || double.IsInfinity(width) || width <= 1)
        {
            width = ActualWidth;
        }

        return Math.Max(1, width - 2);
    }

    private double GetWavePlotWidth()
    {
        return Math.Max(1, GetWaveCanvasWidth() - EegWaveformRenderSurface.WaveLeft - EegWaveformRenderSurface.WaveRightPadding);
    }

    private void AddMarker(EegMarkerTag tag, string source)
    {
        if (viewModel is null || !viewModel.IsRecording)
        {
            FooterHintText.Text = "未开始采集，暂不记录事件标记。";
            return;
        }

        viewModel.AddMarker(tag, source);
    }

    private void RenderMarkerList(IReadOnlyList<EegMarkerRecord> records)
    {
        MarkerListPanel.Children.Clear();
        foreach (var record in records.Reverse())
        {
            AddMarkerListRow(record);
        }

        MarkerCountText.Text = $"{records.Count} 个标记";
    }

    private void AddMarkerListRow(EegMarkerRecord record)
    {
        var row = new Grid { Height = 28, Margin = new Thickness(0, 0, 0, 4) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(82) });

        var dot = new Ellipse
        {
            Width = 9,
            Height = 9,
            Fill = new SolidColorBrush(record.Color),
            VerticalAlignment = VerticalAlignment.Center
        };
        row.Children.Add(dot);

        var name = new TextBlock
        {
            Text = record.Name,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(name, 1);
        row.Children.Add(name);

        var time = new TextBlock
        {
            Text = record.ExperimentTime.ToString(@"hh\:mm\:ss"),
            Foreground = new SolidColorBrush(Color.FromRgb(142, 150, 168)),
            FontFamily = new FontFamily("Consolas"),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        Grid.SetColumn(time, 2);
        row.Children.Add(time);

        MarkerListPanel.Children.Add(row);
    }

    private void EegPage_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var tag = viewModel?.MarkerTags.FirstOrDefault(t => t.KeyText == e.Key.ToString());
        if (tag is null)
        {
            return;
        }

        AddMarker(tag, "keyboard");
        e.Handled = true;
    }

    private void HighlightChannel(int channel)
    {
        if (channel < 0 || currentModel is null || channel >= currentModel.Config.ChannelCount)
        {
            return;
        }

        WaveformSurface.HighlightedChannel = channel;

        foreach (var electrode in electrodeVisuals)
        {
            electrode.Dot.BorderBrush = electrode.ChannelIndex == channel ? Brushes.White : null;
            electrode.Dot.BorderThickness = electrode.ChannelIndex == channel ? new Thickness(2) : new Thickness(0);
        }
    }

    private void ElectrodeCanvas_MouseLeave(object sender, MouseEventArgs e)
    {
        WaveformSurface.HighlightedChannel = -1;

        foreach (var electrode in electrodeVisuals)
        {
            electrode.Dot.BorderThickness = new Thickness(0);
        }
    }

    private void ElectrodeCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        BuildElectrodeMap();
    }

    private void WaveformSurface_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        LayoutWaveforms();
        RenderModel(currentModel);
    }

    private void WaveformScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        LayoutWaveforms();
        RenderModel(currentModel);
    }

    private void TimeAxisCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        DrawTimeAxis();
    }

    private enum ElectrodeStatus
    {
        Good,
        Warn,
        Bad
    }

    private sealed record EegElectrodeCoordinate(
        [property: JsonPropertyName("x_norm")] double XNorm,
        [property: JsonPropertyName("y_norm")] double YNorm,
        [property: JsonPropertyName("x_percent")] double XPercent,
        [property: JsonPropertyName("y_percent")] double YPercent,
        [property: JsonPropertyName("region")] string Region);

    private sealed record ElectrodeVisual(int ChannelIndex, Border Dot);
}
