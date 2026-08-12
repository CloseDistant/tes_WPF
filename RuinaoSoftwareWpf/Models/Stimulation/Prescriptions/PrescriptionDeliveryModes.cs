namespace RuinaoSoftwareWpf;

public static class PrescriptionDeliveryModes
{
    public const string Interval = "间隔";
    public const string Continuous = "连续";
    public static IReadOnlyList<string> All { get; } = [Interval, Continuous];
}
