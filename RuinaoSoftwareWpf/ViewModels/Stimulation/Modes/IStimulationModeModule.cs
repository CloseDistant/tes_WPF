namespace RuinaoSoftwareWpf;

/// <summary>
/// Application-facing boundary for one stimulation mode. Mode-specific pages keep their own
/// parameters, waveform and execution code, while the shell talks only to this contract.
/// </summary>
public interface IStimulationModeModule
{
    StimulationTypeFeatureDefinition Definition { get; }

    ObservableObject PageViewModel { get; }

    event EventHandler? BackRequested;

    event EventHandler<HardwareOperationResult>? HardwareOperationCompleted;

    event EventHandler<StimulationPrescriptionRequestEventArgs>? PrescriptionRequested;

    void PrepareForActivation();

    void ApplyImpedanceSnapshot(IReadOnlyDictionary<int, decimal?> channelImpedanceOhms);

    string GetTargetChannelName(object? targetChannel);

    bool TryApplyPrescription(
        PrescriptionDefinition prescription,
        StimulationPrescriptionApplyScope scope,
        object? targetChannel,
        out string error);
}
