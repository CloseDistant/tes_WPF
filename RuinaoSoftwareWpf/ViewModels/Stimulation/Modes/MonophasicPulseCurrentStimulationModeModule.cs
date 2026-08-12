namespace RuinaoSoftwareWpf;

public sealed class MonophasicPulseCurrentStimulationModeModule
    : StimulationModeModuleBase<MonophasicPulseCurrentControlViewModel>
{
    public MonophasicPulseCurrentStimulationModeModule(
        MonophasicPulseCurrentControlViewModel viewModel)
        : base(StimulationModeCodes.MonophasicPulseCurrent, viewModel)
    {
        viewModel.BackRequested += (_, _) => RaiseBackRequested();
        viewModel.HardwareOperationCompleted += (_, result) => RaiseHardwareOperationCompleted(result);
        viewModel.PrescriptionRequested += (_, eventArgs) => RaisePrescriptionRequested(eventArgs);
    }

    public override string GetTargetChannelName(object? targetChannel) =>
        targetChannel is ChannelConfig channel ? channel.Name : string.Empty;

    public override void ApplyImpedanceSnapshot(
        IReadOnlyDictionary<int, decimal?> channelImpedanceOhms)
    {
        ArgumentNullException.ThrowIfNull(channelImpedanceOhms);
        for (var index = 0; index < ViewModel.Channels.Count; index++)
        {
            ViewModel.Channels[index].UpdateImpedance(channelImpedanceOhms.GetValueOrDefault(index + 1));
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
            ? ViewModel.TryApplyPrescription(prescription, ViewModel.Channels, out error)
            : targetChannel is ChannelConfig channel
                ? ViewModel.TryApplyPrescription(prescription, [channel], out error)
                : Fail(out error);
    }

    private static bool Fail(out string error)
    {
        error = "未找到 M-tPCS 目标通道。";
        return false;
    }
}
