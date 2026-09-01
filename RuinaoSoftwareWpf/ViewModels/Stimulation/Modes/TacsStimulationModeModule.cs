namespace RuinaoSoftwareWpf;

public sealed class TacsStimulationModeModule : StimulationModeModuleBase<TacsControlViewModel>
{
    public TacsStimulationModeModule(TacsControlViewModel viewModel)
        : base(StimulationModeCodes.AlternatingCurrent, viewModel)
    {
        viewModel.BackRequested += (_, _) => RaiseBackRequested();
        viewModel.HardwareOperationCompleted += (_, result) => RaiseHardwareOperationCompleted(result);
        viewModel.PrescriptionRequested += (_, eventArgs) => RaisePrescriptionRequested(eventArgs);
    }

    public override void PrepareForActivation() => ViewModel.RestoreLastSelection();

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

        if (scope == StimulationPrescriptionApplyScope.AllChannels)
        {
            ViewModel.ApplyPrescription(prescription);
            return true;
        }

        if (targetChannel is ChannelConfig channel)
        {
            ViewModel.ApplyPrescription(prescription, channel);
            return true;
        }

        error = "未找到tACS目标通道。";
        return false;
    }
}
