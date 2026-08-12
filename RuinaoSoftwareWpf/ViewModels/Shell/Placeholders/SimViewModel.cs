namespace RuinaoSoftwareWpf;

/// <summary>
/// 仿真页面 ViewModel（占位）。
/// </summary>
public sealed class SimViewModel : ObservableObject
{
    private readonly ISimulationService simulationService;

    public SimViewModel(ISimulationService simulationService)
    {
        this.simulationService = simulationService;
    }
}
