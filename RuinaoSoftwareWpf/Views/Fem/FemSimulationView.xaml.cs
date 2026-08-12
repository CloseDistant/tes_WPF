using System.Windows.Controls;

namespace RuinaoSoftwareWpf.Views;

/// <summary>
/// FEM 仿真页面视图。
/// XAML 负责布局和样式，这个 Code-Behind 文件只调用 InitializeComponent，逻辑交给 FemSimulationViewModel。
/// </summary>
public partial class FemSimulationView : UserControl
{
    public FemSimulationView()
    {
        InitializeComponent();
    }
}
