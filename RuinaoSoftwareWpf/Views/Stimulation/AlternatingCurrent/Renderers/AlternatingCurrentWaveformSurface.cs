using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace RuinaoSoftwareWpf.Views.Renderers;

/// <summary>
/// 按启动时冻结的共享DLL计划绘制TI/tACS模拟波形。
/// 细节视图显示5个载波周期；全程视图显示实际已运行范围内的阶梯包络。
/// </summary>
public sealed class AlternatingCurrentWaveformSurface : FrameworkElement
{
    private static readonly Brush SurfaceBackgroundBrush = Freeze(new SolidColorBrush(Color.FromRgb(14, 21, 32)));
    private static readonly Brush AxisTextBrush = Freeze(new SolidColorBrush(Color.FromRgb(119, 137, 164)));
    private static readonly Brush WaveBrush = Freeze(new SolidColorBrush(Color.FromRgb(77, 174, 255)));
    private static readonly Pen GridPen = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromRgb(34, 45, 61))), 1));
    private static readonly Pen AxisPen = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromRgb(56, 68, 87))), 1));
    private static readonly Pen ZeroPen = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromRgb(69, 83, 104))), 1.2));
    private static readonly Pen WaveGlowPen = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromArgb(42, 77, 174, 255))), 6));
    private static readonly Pen WavePen = Freeze(new Pen(WaveBrush, 2.1));
    private static readonly Pen EnvelopePen = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromRgb(77, 174, 255))), 1.8));

    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(
        nameof(State),
        typeof(AlternatingCurrentWaveformState),
        typeof(AlternatingCurrentWaveformSurface),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnStateChanged));

    public AlternatingCurrentWaveformState? State
    {
        get => (AlternatingCurrentWaveformState?)GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    protected override void OnRender(DrawingContext context)
    {
        base.OnRender(context);
        var width = ActualWidth;
        var height = ActualHeight;
        if (width < 80 || height < 70)
        {
            return;
        }

        context.DrawRectangle(SurfaceBackgroundBrush, null, new Rect(0, 0, width, height));
        var plot = new Rect(50, 12, Math.Max(1, width - 64), Math.Max(1, height - 54));
        DrawGrid(context, plot);

        var state = State;
        if (state is null || !state.HasWaveform || state.Preview is null)
        {
            return;
        }

        DrawYAxis(context, plot, state.Preview.PeakCurrentMilliampere);
        if (state.IsGlobalView)
        {
            DrawEnvelope(context, plot, state, state.Preview);
        }
        else
        {
            DrawCarrierDetail(context, plot, state, state.Preview);
        }
    }

    private static void DrawGrid(DrawingContext context, Rect plot)
    {
        context.DrawRectangle(null, AxisPen, plot);
        for (var index = 1; index < 4; index++)
        {
            var y = plot.Top + plot.Height * index / 4d;
            context.DrawLine(index == 2 ? ZeroPen : GridPen, new Point(plot.Left, y), new Point(plot.Right, y));
        }

        for (var index = 1; index < 6; index++)
        {
            var x = plot.Left + plot.Width * index / 6d;
            context.DrawLine(GridPen, new Point(x, plot.Top), new Point(x, plot.Bottom));
        }
    }

    private static void DrawYAxis(DrawingContext context, Rect plot, double peak)
    {
        DrawRightAlignedText(context, FormatCurrent(peak), plot.Left - 7, plot.Top - 6);
        DrawRightAlignedText(context, "0", plot.Left - 7, plot.Top + plot.Height / 2 - 6);
        DrawRightAlignedText(context, FormatCurrent(-peak), plot.Left - 7, plot.Bottom - 12);
        DrawText(context, "mA", 10, new Point(7, plot.Top - 1));
    }

    private static void DrawCarrierDetail(
        DrawingContext context,
        Rect plot,
        AlternatingCurrentWaveformState state,
        AlternatingCurrentWaveformPreview preview)
    {
        var segment = FindSegment(preview, state.ElapsedSeconds) ?? preview.Segments.First();
        var periodSeconds = 1d / Math.Max(1, preview.FrequencyHz);
        var windowSeconds = periodSeconds * 5d;
        const int sampleCount = 600;
        var geometry = new StreamGeometry();
        using (var geometryContext = geometry.Open())
        {
            for (var index = 0; index <= sampleCount; index++)
            {
                var localSeconds = windowSeconds * index / sampleCount;
                var current = segment.PeakCurrentMilliampere
                    * Math.Sin(2d * Math.PI * preview.FrequencyHz * localSeconds);
                var point = new Point(
                    plot.Left + plot.Width * index / sampleCount,
                    CurrentToY(plot, current, preview.PeakCurrentMilliampere));
                if (index == 0)
                {
                    geometryContext.BeginFigure(point, false, false);
                }
                else
                {
                    geometryContext.LineTo(point, true, false);
                }
            }
        }

        geometry.Freeze();
        context.DrawGeometry(null, WaveGlowPen, geometry);
        context.DrawGeometry(null, WavePen, geometry);
        DrawXAxis(context, plot, 0, windowSeconds * 1000d, "时间 / ms");
    }

    private static void DrawEnvelope(
        DrawingContext context,
        Rect plot,
        AlternatingCurrentWaveformState state,
        AlternatingCurrentWaveformPreview preview)
    {
        var visibleEnd = Math.Clamp(state.ElapsedSeconds, 0, preview.TotalDurationSeconds);
        var axisEnd = Math.Max(0.1, visibleEnd);
        if (visibleEnd > 0)
        {
            DrawEnvelopeSide(context, plot, preview, visibleEnd, axisEnd, positive: true);
            DrawEnvelopeSide(context, plot, preview, visibleEnd, axisEnd, positive: false);
        }

        DrawXAxis(context, plot, 0, axisEnd, "时间 / s");
    }

    private static void DrawEnvelopeSide(
        DrawingContext context,
        Rect plot,
        AlternatingCurrentWaveformPreview preview,
        double visibleEnd,
        double axisEnd,
        bool positive)
    {
        var points = new List<Point>();
        foreach (var segment in preview.Segments)
        {
            var segmentStart = Math.Min(segment.StartSeconds, visibleEnd);
            var segmentEnd = Math.Min(segment.StartSeconds + segment.DurationSeconds, visibleEnd);
            if (segmentEnd <= segmentStart)
            {
                continue;
            }

            var signedCurrent = positive
                ? segment.PeakCurrentMilliampere
                : -segment.PeakCurrentMilliampere;
            points.Add(new Point(
                TimeToX(plot, segmentStart, axisEnd),
                CurrentToY(plot, signedCurrent, preview.PeakCurrentMilliampere)));
            points.Add(new Point(
                TimeToX(plot, segmentEnd, axisEnd),
                CurrentToY(plot, signedCurrent, preview.PeakCurrentMilliampere)));
        }

        if (points.Count == 0)
        {
            return;
        }

        var geometry = new StreamGeometry();
        using (var geometryContext = geometry.Open())
        {
            geometryContext.BeginFigure(points[0], false, false);
            foreach (var point in points.Skip(1))
            {
                geometryContext.LineTo(point, true, false);
            }
        }

        geometry.Freeze();
        context.DrawGeometry(null, EnvelopePen, geometry);
    }

    internal static AlternatingCurrentWaveformSegment? FindSegment(
        AlternatingCurrentWaveformPreview preview,
        double elapsedSeconds)
    {
        if (preview.Segments.Count == 0)
        {
            return null;
        }

        var clamped = Math.Clamp(elapsedSeconds, 0, preview.TotalDurationSeconds);
        return preview.Segments.FirstOrDefault(segment =>
                clamped >= segment.StartSeconds
                && clamped < segment.StartSeconds + segment.DurationSeconds)
            ?? preview.Segments[^1];
    }

    internal static double GetSimulatedCurrent(
        AlternatingCurrentWaveformPreview preview,
        double elapsedSeconds)
    {
        if (elapsedSeconds < 0 || elapsedSeconds >= preview.TotalDurationSeconds)
        {
            return 0;
        }

        var segment = FindSegment(preview, elapsedSeconds);
        return segment is null
            ? 0
            : segment.PeakCurrentMilliampere
                * Math.Sin(2d * Math.PI * preview.FrequencyHz * elapsedSeconds);
    }

    private static void DrawXAxis(DrawingContext context, Rect plot, double start, double end, string title)
    {
        for (var index = 0; index <= 6; index++)
        {
            var value = start + (end - start) * index / 6d;
            var x = plot.Left + plot.Width * index / 6d;
            DrawCenteredText(context, value.ToString("0.###", CultureInfo.InvariantCulture), x, plot.Bottom + 7);
        }

        var text = CreateText(title, 10);
        context.DrawText(text, new Point(plot.Left + (plot.Width - text.Width) / 2, plot.Bottom + 23));
    }

    private static double TimeToX(Rect plot, double seconds, double axisEnd) =>
        plot.Left + seconds / Math.Max(0.000001, axisEnd) * plot.Width;

    private static double CurrentToY(Rect plot, double current, double peak)
    {
        var normalized = current / Math.Max(0.000001, peak);
        return plot.Top + plot.Height / 2d - Math.Clamp(normalized, -1, 1) * plot.Height * 0.42;
    }

    private static string FormatCurrent(double value) =>
        value.ToString("0.000", CultureInfo.InvariantCulture);

    private static void DrawRightAlignedText(DrawingContext context, string text, double right, double y)
    {
        var formatted = CreateText(text, 10);
        context.DrawText(formatted, new Point(right - formatted.Width, y));
    }

    private static void DrawCenteredText(DrawingContext context, string text, double center, double y)
    {
        var formatted = CreateText(text, 10);
        context.DrawText(formatted, new Point(center - formatted.Width / 2, y));
    }

    private static void DrawText(DrawingContext context, string text, double size, Point point) =>
        context.DrawText(CreateText(text, size), point);

    private static FormattedText CreateText(string text, double size) =>
        new(
            text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface("Source Han Sans SC"),
            size,
            AxisTextBrush,
            1);

    private static T Freeze<T>(T value) where T : Freezable
    {
        value.Freeze();
        return value;
    }

    private static void OnStateChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        var surface = (AlternatingCurrentWaveformSurface)dependencyObject;
        if (args.OldValue is INotifyPropertyChanged oldState)
        {
            oldState.PropertyChanged -= surface.OnStatePropertyChanged;
        }

        if (args.NewValue is INotifyPropertyChanged newState)
        {
            newState.PropertyChanged += surface.OnStatePropertyChanged;
        }

        surface.InvalidateVisual();
    }

    private void OnStatePropertyChanged(object? sender, PropertyChangedEventArgs args) => InvalidateVisual();
}
