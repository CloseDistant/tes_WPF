namespace RuinaoSoftwareWpf;

public sealed class StimulationModeRequestedEventArgs(string modeCode) : EventArgs
{
    public string ModeCode { get; } = modeCode;
}
