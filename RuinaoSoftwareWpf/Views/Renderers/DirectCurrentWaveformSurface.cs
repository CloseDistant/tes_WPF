using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace RuinaoSoftwareWpf.Views.Renderers;

/// <summary>
/// 按参数和已运行时间即时绘制 tDCS 模拟波形。绘制点数受控于画布宽度，不随疗程时长增长。
/// </summary>
public sealed class DirectCurrentWaveformSurface : FrameworkElement
{
    private static readonly Brush GridBrush = Freeze(new SolidColorBrush(Color.FromRgb(34, 45, 61)));
    private static readonly Brush AxisTextBrush = Freeze(new SolidColorBrush(Color.FromRgb(119, 137, 164)));
    private static readonly Brush WaveBrush = Freeze(new SolidColorBrush(Color.FromRgb(77, 174, 255)));
    private static readonly Pen GridPen = Freeze(new Pen(GridBrush, 1));
    private static readonly Pen AxisPen = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromRgb(56, 68, 87))), 1));
    private static readonly Pen WaveGlowPen = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromArgb(45, 77, 174, 255))), 6));
    private static readonly Pen WavePen = Freeze(new Pen(WaveBrush, 2.2));

    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(
        nameof(State),
        typeof(DirectCurrentWaveformState),
        typeof(DirectCurrentWaveformSurface),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnStateChanged));

    public DirectCurrentWaveformState? State
    {
        get => (DirectCurrentWaveformState?)GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var width = ActualWidth;
        var height = ActualHeight;
        if (width < 80 || height < 70)
        {
            return;
        }

        const double left = 48;
        const double right = 14;
        const double top = 12;
        const double bottom = 42;
        var plot = new Rect(left, top, Math.Max(1, width - left - right), Math.Max(1, height - top - bottom));
        var state = State;
        if (state is null || !state.HasWaveform || state.Parameters is null)
        {
            DrawGrid(drawingContext, plot, 4);
            return;
        }

        var parameters = state.Parameters;
        var yScale = CreateYScale(parameters);
        DrawGrid(drawingContext, plot, yScale.DivisionCount);
        var elapsed = Math.Clamp(state.ElapsedSeconds, 0, parameters.TotalDurationSeconds);
        var (windowStart, windowEnd) = GetTimeWindow(state, parameters, elapsed);
        DrawAxes(drawingContext, plot, parameters, yScale, windowStart, windowEnd);
        DrawWaveform(drawingContext, plot, state, parameters, yScale, elapsed, windowStart, windowEnd);
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
        DirectCurrentWaveformParameters parameters,
        WaveformYScale yScale,
        double windowStart,
        double windowEnd)
    {
        for (var index = 0; index <= yScale.DivisionCount; index++)
        {
            if (yScale.DivisionCount > 6 && index % 2 != 0)
            {
                continue;
            }

            var value = yScale.Maximum - (yScale.Maximum - yScale.Minimum) * index / yScale.DivisionCount;
            var y = plot.Top + plot.Height * index / yScale.DivisionCount - 6;
            DrawRightAlignedText(context, FormatAxisValue(value), plot.Left - 7, y);
        }

        for (var index = 0; index <= 6; index++)
        {
            var seconds = windowStart + (windowEnd - windowStart) * index / 6d;
            var label = FormatSeconds(seconds);
            var x = plot.Left + plot.Width * index / 6d;
            DrawCenteredText(context, label, x, plot.Bottom + 7);
        }

        DrawText(context, "mA", 10, AxisTextBrush, new Point(6, plot.Top - 1));
        var axisTitle = CreateText("时间 / s", 10, AxisTextBrush);
        context.DrawText(axisTitle, new Point(plot.Left + (plot.Width - axisTitle.Width) / 2, plot.Bottom + 23));

    }

    private static void DrawWaveform(
        DrawingContext context,
        Rect plot,
        DirectCurrentWaveformState state,
        DirectCurrentWaveformParameters parameters,
        WaveformYScale yScale,
        double elapsed,
        double windowStart,
        double windowEnd)
    {
        var visibleEnd = Math.Min(elapsed, windowEnd);
        if (visibleEnd <= windowStart)
        {
            return;
        }

        // 至多约每个水平像素取一个点，避免全程模式随疗程时长增加内存和绘制负担。
        var sampleCount = Math.Clamp((int)Math.Ceiling(plot.Width), 2, 1400);
        var visibleRatio = (visibleEnd - windowStart) / Math.Max(0.001, windowEnd - windowStart);
        sampleCount = Math.Max(2, (int)Math.Ceiling(sampleCount * visibleRatio));

        var geometry = new StreamGeometry();
        using (var geometryContext = geometry.Open())
        {
            for (var index = 0; index < sampleCount; index++)
            {
                var ratio = sampleCount == 1 ? 0 : index / (double)(sampleCount - 1);
                var seconds = windowStart + (visibleEnd - windowStart) * ratio;
                var current = GetSimulatedCurrent(parameters, seconds);
                var point = new Point(
                    plot.Left + (seconds - windowStart) / Math.Max(0.001, windowEnd - windowStart) * plot.Width,
                    CurrentToY(yScale, plot, current));
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

        if (state.RunState == DirectCurrentWaveformRunState.Running)
        {
            var current = GetSimulatedCurrent(parameters, visibleEnd);
            var x = plot.Left + (visibleEnd - windowStart) / Math.Max(0.001, windowEnd - windowStart) * plot.Width;
            var y = CurrentToY(yScale, plot, current);
            context.DrawEllipse(WaveBrush, null, new Point(x, y), 3.5, 3.5);
        }
    }

    internal static double GetSimulatedCurrent(DirectCurrentWaveformParameters parameters, double seconds)
    {
        if (seconds < 0 || seconds > parameters.TotalDurationSeconds)
        {
            return 0;
        }

        if (parameters.IsContinuous)
        {
            var rampDownStart = Math.Max(parameters.RampUpSeconds, parameters.TotalDurationSeconds - parameters.RampDownSeconds);
            if (seconds < parameters.RampUpSeconds)
            {
                return parameters.RampUpSeconds <= 0 ? parameters.CurrentMilliamp : parameters.CurrentMilliamp * seconds / parameters.RampUpSeconds;
            }

            if (seconds < rampDownStart)
            {
                return parameters.CurrentMilliamp;
            }

            return parameters.RampDownSeconds <= 0
                ? 0
                : parameters.CurrentMilliamp * Math.Max(0, parameters.TotalDurationSeconds - seconds) / parameters.RampDownSeconds;
        }

        // PlateauSeconds已由参数层从“单次时长”中扣除了渐升和渐降，
        // 因此一轮长度正好等于用户填写的单次刺激时间加间隔时间。
        var standardCycle = parameters.RampUpSeconds + parameters.PlateauSeconds
            + parameters.RampDownSeconds + parameters.IntervalSeconds;
        if (standardCycle <= 0)
        {
            return 0;
        }

        var cycleIndex = (long)Math.Floor(seconds / standardCycle);
        var cycleStart = cycleIndex * standardCycle;
        var remainingTreatment = parameters.TotalDurationSeconds - cycleStart;

        // 若剩余总时长不足以完成渐升和渐降，则不产生残缺脉冲。
        if (remainingTreatment < parameters.RampUpSeconds + parameters.RampDownSeconds)
        {
            return 0;
        }

        // 最后一轮可缩短恒流平台并提前渐降，保证总时长结束时电流回到 0 mA。
        var plateau = Math.Min(parameters.PlateauSeconds,
            Math.Max(0, remainingTreatment - parameters.RampUpSeconds - parameters.RampDownSeconds));
        var local = seconds - cycleStart;
        var sign = parameters.ReversePolarity && cycleIndex % 2 != 0 ? -1d : 1d;
        if (local < parameters.RampUpSeconds)
        {
            return sign * (parameters.RampUpSeconds <= 0
                ? parameters.CurrentMilliamp
                : parameters.CurrentMilliamp * local / parameters.RampUpSeconds);
        }

        if (local < parameters.RampUpSeconds + plateau)
        {
            return sign * parameters.CurrentMilliamp;
        }

        var rampDownElapsed = local - parameters.RampUpSeconds - plateau;
        if (rampDownElapsed < parameters.RampDownSeconds)
        {
            return sign * (parameters.RampDownSeconds <= 0
                ? 0
                : parameters.CurrentMilliamp * (1 - rampDownElapsed / parameters.RampDownSeconds));
        }

        return 0;
    }

    internal static (double Start, double End) GetTimeWindow(
        DirectCurrentWaveformState state,
        DirectCurrentWaveformParameters parameters,
        double elapsed)
    {
        if (state.IsGlobalView)
        {
            // 全程模式按实际已运行时间扩展横轴，不预先把目标总时长压缩到整张画布。
            return (0, Math.Max(1, elapsed));
        }

        var page = elapsed >= parameters.TotalDurationSeconds
            ? Math.Max(0, Math.Ceiling(parameters.TotalDurationSeconds / 120d) - 1)
            : Math.Floor(elapsed / 120d);
        var start = page * 120d;
        return (start, start + 120d);
    }

    internal static WaveformYScale CreateYScale(DirectCurrentWaveformParameters parameters)
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
        var maximum = (Math.Floor(parameters.CurrentMilliamp / tick) + 1) * tick;
        while (maximum < parameters.CurrentMilliamp * 1.1)
        {
            maximum += tick;
        }

        var hasNegative = parameters.ReversePolarity && !parameters.IsContinuous;
        var minimum = hasNegative ? -maximum : 0;
        var divisions = Math.Max(1, (int)Math.Round((maximum - minimum) / tick));
        return new WaveformYScale(minimum, maximum, tick, divisions);
    }

    private static double CurrentToY(WaveformYScale scale, Rect plot, double current)
    {
        var normalized = (current - scale.Minimum) / Math.Max(0.001, scale.Maximum - scale.Minimum);
        return plot.Bottom - Math.Clamp(normalized, 0, 1) * plot.Height;
    }

    private static string FormatSeconds(double seconds)
    {
        if (seconds >= 3600)
        {
            var span = TimeSpan.FromSeconds(seconds);
            return $"{(int)span.TotalHours}:{span.Minutes:00}";
        }

        return seconds < 10
            ? seconds.ToString("0.0", CultureInfo.InvariantCulture)
            : Math.Round(seconds).ToString("0", CultureInfo.InvariantCulture);
    }

    private static string FormatAxisValue(double value)
    {
        if (Math.Abs(value) < 0.0000001)
        {
            value = 0;
        }

        return value.ToString("0.##", CultureInfo.InvariantCulture);
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
        var surface = (DirectCurrentWaveformSurface)dependencyObject;
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

    internal sealed record WaveformYScale(double Minimum, double Maximum, double TickStep, int DivisionCount);
}
