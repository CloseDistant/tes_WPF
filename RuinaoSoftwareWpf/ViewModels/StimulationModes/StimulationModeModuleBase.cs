namespace RuinaoSoftwareWpf;

public abstract class StimulationModeModuleBase<TViewModel> : IStimulationModeModule
    where TViewModel : ObservableObject
{
    protected StimulationModeModuleBase(string modeCode, TViewModel viewModel)
    {
        Definition = FeatureCatalog.GetStimulationType(modeCode);
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
    }

    public StimulationTypeFeatureDefinition Definition { get; }

    public TViewModel ViewModel { get; }

    public ObservableObject PageViewModel => ViewModel;

    public event EventHandler? BackRequested;

    public event EventHandler<HardwareOperationResult>? HardwareOperationCompleted;

    public event EventHandler<StimulationPrescriptionRequestEventArgs>? PrescriptionRequested;

    public virtual void PrepareForActivation()
    {
    }

    public virtual void ApplyImpedanceSnapshot(
        IReadOnlyDictionary<int, decimal?> channelImpedanceOhms)
    {
        ArgumentNullException.ThrowIfNull(channelImpedanceOhms);
    }

    public abstract string GetTargetChannelName(object? targetChannel);

    public abstract bool TryApplyPrescription(
        PrescriptionDefinition prescription,
        StimulationPrescriptionApplyScope scope,
        object? targetChannel,
        out string error);

    protected void RaiseBackRequested()
    {
        BackRequested?.Invoke(this, EventArgs.Empty);
    }

    protected void RaiseHardwareOperationCompleted(HardwareOperationResult result)
    {
        HardwareOperationCompleted?.Invoke(this, result);
    }

    protected void RaisePrescriptionRequested(StimulationPrescriptionRequestEventArgs eventArgs)
    {
        PrescriptionRequested?.Invoke(this, eventArgs);
    }

    protected bool ValidatePrescriptionMode(
        PrescriptionDefinition prescription,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(prescription);
        if (string.Equals(
                prescription.StimulationType,
                Definition.ModeCode,
                StringComparison.Ordinal))
        {
            error = string.Empty;
            return true;
        }

        error = $"处方类型 {prescription.StimulationType} 与当前模式 {Definition.ModeCode} 不一致。";
        return false;
    }
}
