namespace RuinaoSoftwareWpf;

/// <summary>
/// Remembers the stimulation page used during the current login session.
/// The mode code is not persisted to the database or configuration files.
/// </summary>
public sealed class StimulationNavigationState
{
    public string? CurrentModeCode { get; private set; }

    public bool IsTypeSelection => CurrentModeCode is null;

    public void RememberMode(string modeCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modeCode);
        CurrentModeCode = modeCode;
    }

    public void RememberTypeSelection()
    {
        CurrentModeCode = null;
    }

    public void Reset()
    {
        RememberTypeSelection();
    }
}
