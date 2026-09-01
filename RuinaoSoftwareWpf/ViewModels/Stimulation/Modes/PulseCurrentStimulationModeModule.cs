namespace RuinaoSoftwareWpf;

public sealed class PulseCurrentStimulationModeModule
    : StimulationModeModuleBase<PulseCurrentControlViewModel>
{
    public PulseCurrentStimulationModeModule(PulseCurrentControlViewModel viewModel)
        : base(StimulationModeCodes.PulseCurrent, viewModel)
    {
        viewModel.BackRequested += (_, _) => RaiseBackRequested();
        viewModel.HardwareOperationCompleted += (_, result) => RaiseHardwareOperationCompleted(result);
        viewModel.PrescriptionRequested += (_, eventArgs) =>
            RaisePrescriptionRequested(eventArgs);
    }

    public override string GetTargetChannelName(object? targetChannel)
    {
        return targetChannel is PulseCurrentChannelConfig channel ? channel.Name : string.Empty;
    }

    public override void ApplyImpedanceSnapshot(
        IReadOnlyDictionary<int, decimal?> channelImpedanceOhms)
    {
        ArgumentNullException.ThrowIfNull(channelImpedanceOhms);
        for (var index = 0; index < ViewModel.Channels.Count; index++)
        {
            ViewModel.Channels[index].UpdateImpedance(
                channelImpedanceOhms.GetValueOrDefault(index + 1));
        }
    }

    public override bool TryApplyPrescription(
        PrescriptionDefinition prescription,
        StimulationPrescriptionApplyScope scope,
        object? targetChannel,
        out string error)
    {
        if (!ValidatePrescriptionMode(prescription, out error))
        {
            return false;
        }

        return scope == StimulationPrescriptionApplyScope.AllChannels
            ? ViewModel.TryApplyPrescription(prescription, out error)
            : targetChannel is PulseCurrentChannelConfig channel
                ? ViewModel.TryApplyPrescription(prescription, channel, out error)
                : FailTarget(out error);
    }

    private static bool FailTarget(out string error)
    {
        error = "未找到 tPCS 目标通道。";
        return false;
    }
}
