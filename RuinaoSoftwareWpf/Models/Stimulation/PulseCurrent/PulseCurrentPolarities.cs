namespace RuinaoSoftwareWpf;

public static class PulseCurrentPolarities
{
    public const string NotReversed = "不掉转";
    public const string Reversed = "调转";
    public static IReadOnlyList<string> All { get; } = [NotReversed, Reversed];
}
