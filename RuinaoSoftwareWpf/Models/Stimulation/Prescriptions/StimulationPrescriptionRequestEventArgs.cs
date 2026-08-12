namespace RuinaoSoftwareWpf;

/// <summary>刺激页面请求选择处方时携带的目标范围。</summary>
public sealed class StimulationPrescriptionRequestEventArgs : EventArgs
{
    public StimulationPrescriptionRequestEventArgs(
        string stimulationType,
        StimulationPrescriptionApplyScope scope,
        object? targetChannel = null)
    {
        StimulationType = stimulationType;
        Scope = scope;
        TargetChannel = targetChannel;
    }

    public string StimulationType { get; }

    public StimulationPrescriptionApplyScope Scope { get; }

    public object? TargetChannel { get; }

    public bool AppliesToAllChannels => Scope == StimulationPrescriptionApplyScope.AllChannels;
}
