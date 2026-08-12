namespace RuinaoSoftwareWpf;

internal sealed record StimulationImpedanceStartAssessment<TChannel>(
    IReadOnlyList<TChannel> EligibleChannels,
    IReadOnlyList<TChannel> WarningChannels,
    IReadOnlyList<TChannel> CriticalChannels,
    IReadOnlyList<TChannel> UnavailableChannels)
    where TChannel : IStimulationImpedanceChannel
{
    public bool RequiresConfirmation =>
        WarningChannels.Count > 0
        || CriticalChannels.Count > 0
        || UnavailableChannels.Count > 0;
}
