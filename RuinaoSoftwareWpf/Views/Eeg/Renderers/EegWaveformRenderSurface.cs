using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace RuinaoSoftwareWpf.Views.Renderers;

public sealed class EegWaveformRenderSurface : FrameworkElement
{
    public const double ChannelLaneHeight = 18.0;
    public const double WaveLeft = 38.0;
    public const double WaveRightPadding = 18.0;
    public const double WaveTop = 18.0;
    public const double WaveBottom = 28.0;

    private const int MaxDrawPointsPerChannel = 320;
    private const double MockAmplitudeScale = 0.08;

    private static readonly Brush BackgroundBrush = new SolidColorBrush(Color.FromRgb(13, 16, 23));
    private static readonly Brush LabelBrush = new SolidColorBrush(Color.FromRgb(142, 150, 168));
    private static readonly Pen ScanPen = new(new SolidColorBrush(Color.FromRgb(208, 144, 62)), 1.4);
    private static readonly Typeface LabelTypeface = new("Consolas");

    private static readonly Pen[] BandPens =
    {
        new(new SolidColorBrush(Color.FromRgb(208, 144, 62)), 1),
        new(new SolidColorBrush(Color.FromRgb(93, 218, 119)), 1),
        new(new SolidColorBrush(Color.FromRgb(228, 78, 79)), 1),
        new(new SolidColorBrush(Color.FromRgb(142, 150, 168)), 1),
        new(new SolidColorBrush(Color.FromRgb(245, 198, 96)), 1)
    };

    private static readonly Pen[] HighlightPens =
    {
        new(BandPens[0].Brush, 2.0),
        new(BandPens[1].Brush, 2.0),
        new(BandPens[2].Brush, 2.0),
        new(BandPens[3].Brush, 2.0),
        new(BandPens[4].Brush, 2.0)
    };

    static EegWaveformRenderSurface()
    {
        BackgroundBrush.Freeze();
        LabelBrush.Freeze();
        ScanPen.Freeze();
        foreach (var pen in BandPens)
        {
            pen.Freeze();
        }

        foreach (var pen in HighlightPens)
        {
            pen.Freeze();
        }
    }

    public EegWaveformRenderModel? Model
    {
        get => (EegWaveformRenderModel?)GetValue(ModelProperty);
        set => SetValue(ModelProperty, value);
    }

    public static readonly DependencyProperty ModelProperty =
        DependencyProperty.Register(
            nameof(Model),
            typeof(EegWaveformRenderModel),
            typeof(EegWaveformRenderSurface),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public int HighlightedChannel
    {
        get => (int)GetValue(HighlightedChannelProperty);
        set => SetValue(HighlightedChannelProperty, value);
    }

    public static readonly DependencyProperty HighlightedChannelProperty =
        DependencyProperty.Register(
            nameof(HighlightedChannel),
            typeof(int),
            typeof(EegWaveformRenderSurface),
            new FrameworkPropertyMetadata(-1, FrameworkPropertyMetadataOptions.AffectsRender));

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        drawingContext.DrawRectangle(BackgroundBrush, null, new Rect(0, 0, ActualWidth, ActualHeight));

        if (Model is null)
        {
            return;
        }

        DrawChannels(drawingContext, Model);
        DrawScanLine(drawingContext, Model);
        DrawMarkers(drawingContext, Model);
    }

    private void DrawChannels(DrawingContext drawingContext, EegWaveformRenderModel model)
    {
        var sampleCount = model.TotalSamples >= model.Config.PageSampleCount
            ? model.Config.PageSampleCount
            : model.PageSampleIndex;
        var drawStep = Math.Max(1, sampleCount / MaxDrawPointsPerChannel);
        var waveWidth = GetPlotWidth();
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        for (var channel = 0; channel < model.Config.ChannelCount; channel++)
        {
            var baseline = WaveTop + channel * ChannelLaneHeight;
            DrawChannelLabel(drawingContext, model.Config.ChannelNames[channel], baseline, pixelsPerDip);
            DrawChannelLine(drawingContext, model, channel, sampleCount, drawStep, waveWidth, baseline);
        }
    }

    private static void DrawChannelLabel(DrawingContext drawingContext, string name, double baseline, double pixelsPerDip)
    {
        var text = new FormattedText(
            name,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            LabelTypeface,
            10,
            LabelBrush,
            pixelsPerDip);
        drawingContext.DrawText(text, new Point(4, baseline - 7));
    }

    private void DrawChannelLine(
        DrawingContext drawingContext,
        EegWaveformRenderModel model,
        int channel,
        int sampleCount,
        int drawStep,
        double waveWidth,
        double baseline)
    {
        if (sampleCount <= 0)
        {
            return;
        }

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            var started = false;
            for (var sample = 0; sample < sampleCount; sample += drawStep)
            {
                var x = WaveLeft + sample * waveWidth / model.Config.PageSampleCount;
                var y = baseline - model.PageSamples[channel][sample] * MockAmplitudeScale;
                if (!started)
                {
                    context.BeginFigure(new Point(x, y), false, false);
                    started = true;
                }
                else
                {
                    context.LineTo(new Point(x, y), true, false);
                }
            }
        }

        geometry.Freeze();
        var pen = BandPens[channel / 14 % BandPens.Length];
        if (HighlightedChannel >= 0 && HighlightedChannel != channel)
        {
            drawingContext.PushOpacity(0.18);
            drawingContext.DrawGeometry(null, pen, geometry);
            drawingContext.Pop();
            return;
        }

        drawingContext.DrawGeometry(null, HighlightedChannel == channel ? HighlightPens[channel / 14 % HighlightPens.Length] : pen, geometry);
    }

    private void DrawScanLine(DrawingContext drawingContext, EegWaveformRenderModel model)
    {
        var x = WaveLeft + model.PageSampleIndex * GetPlotWidth() / model.Config.PageSampleCount;
        drawingContext.DrawLine(ScanPen, new Point(x, 0), new Point(x, ActualHeight));
    }

    private void DrawMarkers(DrawingContext drawingContext, EegWaveformRenderModel model)
    {
        var waveWidth = GetPlotWidth();
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        foreach (var marker in model.CurrentPageMarkers)
        {
            var x = WaveLeft + marker.PageSampleIndex * waveWidth / model.Config.PageSampleCount;
            var brush = new SolidColorBrush(marker.Color);
            brush.Freeze();
            var pen = new Pen(brush, 1.2);
            pen.Freeze();
            drawingContext.DrawLine(pen, new Point(x, 0), new Point(x, ActualHeight));

            var label = new FormattedText(
                marker.Name,
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                LabelTypeface,
                11,
                brush,
                pixelsPerDip);
            drawingContext.DrawText(label, new Point(x + 4, 6));
        }
    }

    private double GetPlotWidth()
    {
        return Math.Max(1, ActualWidth - WaveLeft - WaveRightPadding);
    }
}
