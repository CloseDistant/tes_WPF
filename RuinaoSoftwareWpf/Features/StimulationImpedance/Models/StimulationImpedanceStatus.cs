using System.Windows.Media;

namespace RuinaoSoftwareWpf;

public enum StimulationImpedanceStatus
{
    Unavailable,
    Normal,
    Warning,
    Critical,
}

internal interface IStimulationImpedanceChannel
{
    string Name { get; }

    decimal? ImpedanceOhms { get; }

    StimulationImpedanceStatus ImpedanceStatus { get; }
}

internal static class StimulationImpedancePresentation
{
    private static readonly Brush GrayBrush = CreateBrush(0x5F, 0x6B, 0x7D);
    private static readonly Brush GreenBrush = CreateBrush(0x5D, 0xDA, 0x77);
    private static readonly Brush BlueBrush = CreateBrush(0x5A, 0x9F, 0xF2);
    private static readonly Brush YellowBrush = CreateBrush(0xFF, 0xD8, 0x4D);
    private static readonly Brush RedBrush = CreateBrush(0xE8, 0x4E, 0x4F);

    public static StimulationImpedanceStatus GetStatus(decimal? impedanceOhms)
    {
        if (!impedanceOhms.HasValue)
        {
            return StimulationImpedanceStatus.Unavailable;
        }

        if (impedanceOhms.Value <= 10_000m)
        {
            return StimulationImpedanceStatus.Normal;
        }

        return impedanceOhms.Value <= 20_000m
            ? StimulationImpedanceStatus.Warning
            : StimulationImpedanceStatus.Critical;
    }

    public static Brush GetImpedanceBrush(StimulationImpedanceStatus status) => status switch
    {
        StimulationImpedanceStatus.Normal => GreenBrush,
        StimulationImpedanceStatus.Warning => YellowBrush,
        StimulationImpedanceStatus.Critical => RedBrush,
        _ => GrayBrush,
    };

    public static Brush GetStatusIndicatorBrush(
        StimulationImpedanceStatus status,
        bool isStimulating) =>
        status == StimulationImpedanceStatus.Normal && isStimulating
            ? BlueBrush
            : GetImpedanceBrush(status);

    private static Brush CreateBrush(byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }
}
