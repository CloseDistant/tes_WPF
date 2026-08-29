using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace RuinaoSoftwareWpf.Views.Renderers;

/// <summary>
/// 按 tPCS 参数快照和已运行时间即时绘制模拟波形，不保存历史采样点。
/// </summary>
public sealed class PulseCurrentWaveformSurface : FrameworkElement
{
    private static readonly Brush GridBrush = Freeze(new SolidColorBrush(Color.FromRgb(34, 45, 61)));
    private static readonly Brush AxisTextBrush = Freeze(new SolidColorBrush(Color.FromRgb(119, 137, 164)));
    private static readonly Brush WaveBrush = Freeze(new SolidColorBrush(Color.FromRgb(77, 174, 255)));
    private static readonly Brush CountBrush = Freeze(new SolidColorBrush(Color.FromRgb(229, 163, 60)));
    private static readonly Pen GridPen = Freeze(new Pen(GridBrush, 1));
    private static readonly Pen AxisPen = Freeze(new Pen(
        Freeze(new SolidColorBrush(Color.FromRgb(56, 68, 87))),
        1));
    private static readonly Pen WaveGlowPen = Freeze(new Pen(
        Freeze(new SolidColorBrush(Color.FromArgb(45, 77, 174, 255))),
        6));
    private static readonly Pen WavePen = Freeze(new Pen(WaveBrush, 2.2));

    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(
        nameof(State),
        typeof(PulseCurrentWaveformState),
        typeof(PulseCurrentWaveformSurface),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnStateChanged));

    public PulseCurrentWaveformState? State
    {
        get => (PulseCurrentWaveformState?)GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        if (ActualWidth < 80 || ActualHeight < 70)
        {
            return;
        }

        const double left = 48;
        const double right = 14;
        const double top = 12;
        const double bottom = 42;
        var plot = new Rect(
            left,
            top,
            Math.Max(1, ActualWidth - left - right),
            Math.Max(1, ActualHeight - top - bottom));
        var state = State;
        if (state is null || !state.HasWaveform || state.Parameters is null)
        {
            DrawGrid(drawingContext, plot, 5);
            return;
        }

        var parameters = state.Parameters;
        var scale = CreateYScale(parameters);
        var elapsed = Math.Clamp(state.ElapsedSeconds, 0, parameters.TotalRuntimeSeconds);
        var (windowStart, windowEnd) = GetTimeWindow(state, parameters, elapsed);

        DrawGrid(drawingContext, plot, scale.DivisionCount);
        DrawAxes(drawingContext, plot, scale, windowStart, windowEnd);
        DrawWaveform(
            drawingContext,
            plot,
            parameters,
            scale,
            elapsed,
            windowStart,
            windowEnd);
        DrawPulseCount(drawingContext, state.PulseCountDisplay, plot);
    }

    internal static (double Start, double End) GetTimeWindow(
        PulseCurrentWaveformState state,
        PulseCurrentParameters parameters,
        double elapsedSeconds)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(parameters);
        var elapsed = Math.Clamp(elapsedSeconds, 0, parameters.TotalRuntimeSeconds);
        if (state.IsGlobalView)
        {
            // 全程模式只压缩已经实际运行的部分，急停后固定在急停时刻。
            return (0, Math.Max(0.001, elapsed));
        }

        // 毫秒级脉冲在固定 60 秒窗口中容易被压成色块。细节模式根据周期
        // 展示最近约 8 次脉冲，同时限制窗口过短或过长。
        var cycleSeconds = (parameters.PulseWidthMilliseconds
            + parameters.IntervalWidthMilliseconds) / 1000d;
        var detailDuration = Math.Clamp(cycleSeconds * 8d, 0.5, 60d);
        var end = Math.Max(0.001, elapsed);
        return (Math.Max(0, end - detailDuration), end);
    }

    private static void DrawGrid(DrawingContext context, Rect plot, int horizontalDivisions)
    {
        context.DrawRectangle(null, AxisPen, plot);
        for (var index = 1; index < horizontalDivisions; index++)
        {
            var y = plot.Top + plot.Height * index / horizontalDivisions;
            context.DrawLine(GridPen, new Point(plot.Left, y), new Point(plot.Right, y));
        }

        for (var index = 1; index < 6; index++)
        {
            var x = plot.Left + plot.Width * index / 6d;
            context.DrawLine(GridPen, new Point(x, plot.Top), new Point(x, plot.Bottom));
        }
    }

    private static void DrawAxes(
        DrawingContext context,
        Rect plot,
        WaveformYScale scale,
        double windowStart,
        double windowEnd)
    {
        for (var index = 0; index <= scale.DivisionCount; index++)
        {
            var value = scale.Maximum
                - (scale.Maximum - scale.Minimum) * index / scale.DivisionCount;
            var y = plot.Top + plot.Height * index / scale.DivisionCount - 6;
            DrawRightAlignedText(context, FormatAxisValue(value), plot.Left - 7, y);
        }

        for (var index = 0; index <= 6; index++)
        {
            var seconds = windowStart + (windowEnd - windowStart) * index / 6d;
            var x = plot.Left + plot.Width * index / 6d;
            DrawCenteredText(context, FormatSeconds(seconds), x, plot.Bottom + 7);
        }

        DrawText(context, "mA", 10, AxisTextBrush, new Point(6, plot.Top - 1));
        var axisTitle = CreateText("时间 / s", 10, AxisTextBrush);
        context.DrawText(
            axisTitle,
            new Point(plot.Left + (plot.Width - axisTitle.Width) / 2, plot.Bottom + 23));
    }

    private static void DrawWaveform(
        DrawingContext context,
        Rect plot,
        PulseCurrentParameters parameters,
        WaveformYScale scale,
        double elapsed,
        double windowStart,
        double windowEnd)
    {
        var visibleEnd = Math.Min(elapsed, windowEnd);
        if (visibleEnd <= windowStart)
        {
            return;
        }

        var points = CreateWaveformPoints(
            parameters,
            windowStart,
            visibleEnd,
            plot.Width);
        if (points.Count < 2)
        {
            return;
        }

        var geometry = new StreamGeometry();
        using (var geometryContext = geometry.Open())
        {
            var hasOpenFigure = false;
            for (var index = 0; index < points.Count; index++)
            {
                if (double.IsNaN(points[index].CurrentMilliamp))
                {
                    hasOpenFigure = false;
                    continue;
                }

                var point = new Point(
                    plot.Left
                        + (points[index].Seconds - windowStart)
                        / Math.Max(0.001, windowEnd - windowStart)
                        * plot.Width,
                    CurrentToY(scale, plot, points[index].CurrentMilliamp));
                if (!hasOpenFigure)
                {
                    geometryContext.BeginFigure(point, false, false);
                    hasOpenFigure = true;
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
    }

    internal static IReadOnlyList<WaveformPoint> CreateWaveformPoints(
        PulseCurrentParameters parameters,
        double elapsed,
        double plotWidth)
        => CreateWaveformPoints(parameters, 0, elapsed, plotWidth);

    internal static IReadOnlyList<WaveformPoint> CreateWaveformPoints(
        PulseCurrentParameters parameters,
        double visibleStart,
        double visibleEnd,
        double plotWidth)
    {
        visibleStart = Math.Clamp(visibleStart, 0, parameters.TotalRuntimeSeconds);
        visibleEnd = Math.Clamp(visibleEnd, visibleStart, parameters.TotalRuntimeSeconds);
        if (visibleEnd <= visibleStart)
        {
            return [];
        }

        var rise = parameters.RiseWidthMilliseconds / 1000d;
        var pulse = parameters.PulseWidthMilliseconds / 1000d;
        var interval = parameters.IntervalWidthMilliseconds / 1000d;
        var cycle = pulse + interval;
        var estimatedVisiblePulses = cycle <= 0
            ? 0
            : Math.Max(0, (long)Math.Ceiling((visibleEnd - Math.Max(visibleStart, rise)) / cycle)) + 1;
        if (estimatedVisiblePulses > 700)
        {
            return CreateBoundedSample(parameters, visibleStart, visibleEnd, plotWidth);
        }

        var amplitude = string.Equals(
            parameters.Polarity,
            PulseCurrentPolarities.Reversed,
            StringComparison.Ordinal)
            ? -parameters.CurrentMilliamp
            : parameters.CurrentMilliamp;
        var points = new List<WaveformPoint>((int)Math.Min(estimatedVisiblePulses * 5 + 8, 3508));

        AddRampSegment(points, visibleStart, visibleEnd, rise, amplitude);

        if (parameters.PlannedTotalCount > 0)
        {
            var firstVisibleIndex = cycle <= 0
                ? 0
                : Math.Max(
                    0,
                    (long)Math.Floor((visibleStart - rise) / cycle));
            for (var pulseIndex = firstVisibleIndex;
                 pulseIndex < parameters.PlannedTotalCount;
                 pulseIndex++)
            {
                var start = rise + pulseIndex * cycle;
                if (start > visibleEnd)
                {
                    break;
                }

                AddConstantSegment(
                    points,
                    visibleStart,
                    visibleEnd,
                    start,
                    start + pulse,
                    amplitude);
            }
        }

        return points;
    }

    private static void AddRampSegment(
        List<WaveformPoint> points,
        double visibleStart,
        double visibleEnd,
        double riseSeconds,
        double amplitude)
    {
        if (riseSeconds <= 0)
        {
            return;
        }

        var segmentStart = Math.Max(visibleStart, 0);
        var segmentEnd = Math.Min(visibleEnd, riseSeconds);
        if (segmentEnd < segmentStart)
        {
            return;
        }

        AddBreak(points);
        var startCurrent = amplitude * segmentStart / riseSeconds;
        AddPoint(points, segmentStart, startCurrent);
        AddPoint(points, segmentEnd, amplitude * segmentEnd / riseSeconds);
    }

    private static void AddConstantSegment(
        List<WaveformPoint> points,
        double visibleStart,
        double visibleEnd,
        double segmentStart,
        double segmentEnd,
        double currentMilliamp)
    {
        var clippedStart = Math.Max(visibleStart, segmentStart);
        var clippedEnd = Math.Min(visibleEnd, segmentEnd);
        if (clippedEnd < clippedStart)
        {
            return;
        }

        AddBreak(points);
        AddPoint(points, clippedStart, currentMilliamp);
        AddPoint(points, clippedEnd, currentMilliamp);
    }

    private static void AddBreak(List<WaveformPoint> points)
    {
        if (points.Count > 0 && !double.IsNaN(points[^1].CurrentMilliamp))
        {
            points.Add(new WaveformPoint(points[^1].Seconds, double.NaN));
        }
    }

    private static IReadOnlyList<WaveformPoint> CreateBoundedSample(
        PulseCurrentParameters parameters,
        double visibleStart,
        double visibleEnd,
        double plotWidth)
    {
        var sampleCount = Math.Clamp((int)Math.Ceiling(plotWidth), 2, 1400);
        var points = new List<WaveformPoint>(sampleCount * 2);
        var wasActive = false;
        for (var index = 0; index < sampleCount; index++)
        {
            var seconds = visibleStart
                + (visibleEnd - visibleStart) * index / (sampleCount - 1d);
            var current = PulseCurrentWaveformMath.GetSimulatedCurrent(parameters, seconds);
            var isActive = Math.Abs(current) > 0.000001;
            if (!isActive)
            {
                if (wasActive)
                {
                    AddBreak(points);
                }

                wasActive = false;
                continue;
            }

            if (!wasActive)
            {
                AddBreak(points);
            }

            points.Add(new WaveformPoint(seconds, current));
            wasActive = true;
        }

        return points;
    }

    private static void AddPointInWindow(
        List<WaveformPoint> points,
        double visibleStart,
        double visibleEnd,
        double seconds,
        double current)
    {
        if (seconds >= visibleStart && seconds <= visibleEnd)
        {
            AddPoint(points, seconds, current);
        }
    }

    private static void AddPoint(List<WaveformPoint> points, double seconds, double current)
    {
        var point = new WaveformPoint(seconds, current);
        if (points.Count == 0 || points[^1] != point)
        {
            points.Add(point);
        }
    }

    private static void DrawPulseCount(DrawingContext context, string text, Rect plot)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var formatted = CreateText(text, 11, CountBrush);
        context.DrawText(formatted, new Point(plot.Right - formatted.Width, plot.Top + 8));
    }

    internal static WaveformYScale CreateYScale(PulseCurrentParameters parameters)
    {
        var rawStep = Math.Max(parameters.CurrentMilliamp / 4d, 0.001);
        var exponent = Math.Pow(10, Math.Floor(Math.Log10(rawStep)));
        var fraction = rawStep / exponent;
        var niceFraction = fraction <= 1 ? 1
            : fraction <= 2 ? 2
            : fraction <= 2.5 ? 2.5
            : fraction <= 5 ? 5
            : 10;
        var tick = niceFraction * exponent;
        var magnitude = (Math.Floor(parameters.CurrentMilliamp / tick) + 1) * tick;
        while (magnitude < parameters.CurrentMilliamp * 1.1)
        {
            magnitude += tick;
        }

        var negative = string.Equals(
            parameters.Polarity,
            PulseCurrentPolarities.Reversed,
            StringComparison.Ordinal);
        return negative
            ? new WaveformYScale(-magnitude, 0, tick, Math.Max(1, (int)Math.Round(magnitude / tick)))
            : new WaveformYScale(0, magnitude, tick, Math.Max(1, (int)Math.Round(magnitude / tick)));
    }

    private static double CurrentToY(WaveformYScale scale, Rect plot, double current)
    {
        var normalized = (current - scale.Minimum)
            / Math.Max(0.001, scale.Maximum - scale.Minimum);
        return plot.Bottom - Math.Clamp(normalized, 0, 1) * plot.Height;
    }

    internal static string FormatSeconds(double seconds) =>
        seconds.ToString("0.0", CultureInfo.InvariantCulture);

    internal static string FormatAxisValue(double value)
    {
        if (Math.Abs(value) < 0.0000001)
        {
            value = 0;
        }

        return value.ToString("0.0#", CultureInfo.InvariantCulture);
    }

    private static void DrawRightAlignedText(DrawingContext context, string text, double right, double y)
    {
        var formatted = CreateText(text, 10, AxisTextBrush);
        context.DrawText(formatted, new Point(right - formatted.Width, y));
    }

    private static void DrawCenteredText(DrawingContext context, string text, double center, double y)
    {
        var formatted = CreateText(text, 10, AxisTextBrush);
        context.DrawText(formatted, new Point(center - formatted.Width / 2, y));
    }

    private static void DrawText(DrawingContext context, string text, double size, Brush brush, Point point)
        => context.DrawText(CreateText(text, size, brush), point);

    private static FormattedText CreateText(string text, double size, Brush brush)
    {
        return new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface("Microsoft YaHei UI"),
            size,
            brush,
            1);
    }

    private static T Freeze<T>(T value) where T : Freezable
    {
        value.Freeze();
        return value;
    }

    private static void OnStateChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        var surface = (PulseCurrentWaveformSurface)dependencyObject;
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

    internal sealed record WaveformPoint(double Seconds, double CurrentMilliamp);

    internal sealed record WaveformYScale(
        double Minimum,
        double Maximum,
        double TickStep,
        int DivisionCount);
}
