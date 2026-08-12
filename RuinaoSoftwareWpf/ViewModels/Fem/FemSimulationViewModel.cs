namespace RuinaoSoftwareWpf;

/// <summary>
/// FEM 仿真页面 ViewModel。
/// 目前主要是占位，后续接入 ISimulationService 调用 FEM 计算。
/// </summary>
public sealed class FemSimulationViewModel : ObservableObject
{
    private readonly ISimulationService simulationService;
    private IMainUiContext? context;

    public FemSimulationViewModel(ISimulationService simulationService)
    {
        this.simulationService = simulationService;
    }

    /// <summary>主界面上下文。必须在 Initialize 后使用。</summary>
    public IMainUiContext Context =>
        context ?? throw new InvalidOperationException("FEM simulation context has not been initialized.");

    /// <summary>由 MainViewModel 调用，传入主界面上下文。</summary>
    public void Initialize(IMainUiContext uiContext)
    {
        context = uiContext;
        OnPropertyChanged(nameof(Context));
    }
}
