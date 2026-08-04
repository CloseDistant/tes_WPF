namespace RuinaoSoftwareWpf;

/// <summary>仅在Debug模拟联机期间生成确定性的CH1～CH16阻抗。</summary>
public sealed class DebugStimulationImpedanceProvider : IDebugStimulationImpedanceProvider
{
    private const decimal FirstChannelOhms = 500m;
    private const decimal ChannelStepOhms = 20m;
    private const int ChannelCount = 16;

    private readonly IDebugHardwareSimulationService debugHardwareSimulation;

    public DebugStimulationImpedanceProvider(
        IDebugHardwareSimulationService debugHardwareSimulation)
    {
        this.debugHardwareSimulation = debugHardwareSimulation;
    }

    public StimulationImpedanceSnapshot? GetSnapshot()
    {
#if DEBUG && !EXHIBITION
        if (!debugHardwareSimulation.IsConnected)
        {
            return null;
        }

        var capturedAt = DateTimeOffset.Now;
        var channels = Enumerable.Range(1, ChannelCount)
            .Select(logicalChannelNumber => new StimulationImpedanceChannelSnapshot(
                logicalChannelNumber,
                BoardSlotIndex: null,
                BoardAddress: null,
                PhysicalChannelNumber: null,
                RegisterAddress: null,
                RawValue: null,
                ImpedanceOhms: FirstChannelOhms
                    + ((logicalChannelNumber - 1) * ChannelStepOhms),
                LastSuccessfulReadAt: capturedAt))
            .ToArray();
        return new StimulationImpedanceSnapshot(capturedAt, channels);
#else
        return null;
#endif
    }
}
