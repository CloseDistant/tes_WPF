namespace RuinaoSoftwareWpf;

using System.Globalization;
using System.Windows.Media;

public sealed class StimulationImpedanceDiagnosticChannelViewModel
{
    public StimulationImpedanceDiagnosticChannelViewModel(
        StimulationImpedanceChannelSnapshot snapshot)
    {
        Snapshot = snapshot;
        ChannelText = $"CH {snapshot.LogicalChannelNumber}";
        Status = StimulationImpedancePresentation.GetStatus(snapshot.ImpedanceOhms);
        StatusBrush = StimulationImpedancePresentation.GetImpedanceBrush(Status);
        StatusText = Status switch
        {
            StimulationImpedanceStatus.Normal => "阻抗正常",
            StimulationImpedanceStatus.Warning => "阻抗偏高",
            StimulationImpedanceStatus.Critical => "阻抗过高",
            _ => "阻抗不可用",
        };
        ImpedanceText = FormatImpedance(snapshot.ImpedanceOhms);
        BoardText = snapshot.BoardSlotIndex.HasValue && snapshot.BoardAddress.HasValue
            ? $"槽位{snapshot.BoardSlotIndex.Value} / 0x{snapshot.BoardAddress.Value:X2}"
            : "—";
        PhysicalChannelText = snapshot.PhysicalChannelNumber.HasValue
            ? $"CH{snapshot.PhysicalChannelNumber.Value}"
            : "—";
        LastReadText = snapshot.LastSuccessfulReadAt?.ToLocalTime().ToString("HH:mm:ss") ?? "—";
        RegisterText = snapshot.RegisterAddress.HasValue
            ? $"0x{snapshot.RegisterAddress.Value:X4}"
            : "—";
        RawHexText = snapshot.RawValue.HasValue
            ? $"0x{snapshot.RawValue.Value:X8}"
            : "—";
        RawDecimalText = snapshot.RawValue?.ToString(CultureInfo.InvariantCulture) ?? "—";
        ConversionText = snapshot.RawValue switch
        {
            0 => "原始值 0 → 不可用",
            { } raw => $"{raw} ÷ 100 = {snapshot.ImpedanceOhms:0.##} Ω",
            _ => "—",
        };
    }

    public StimulationImpedanceChannelSnapshot Snapshot { get; }
    public int LogicalChannelNumber => Snapshot.LogicalChannelNumber;
    public string ChannelText { get; }
    public StimulationImpedanceStatus Status { get; }
    public Brush StatusBrush { get; }
    public string StatusText { get; }
    public string ImpedanceText { get; }
    public string BoardText { get; }
    public string PhysicalChannelText { get; }
    public string LastReadText { get; }
    public string RegisterText { get; }
    public string RawHexText { get; }
    public string RawDecimalText { get; }
    public string ConversionText { get; }

    private static string FormatImpedance(decimal? impedanceOhms)
    {
        if (!impedanceOhms.HasValue)
        {
            return "—";
        }

        return impedanceOhms.Value >= 1_000m
            ? $"{impedanceOhms.Value / 1_000m:0.00} kΩ"
            : $"{impedanceOhms.Value:0.##} Ω";
    }
}
