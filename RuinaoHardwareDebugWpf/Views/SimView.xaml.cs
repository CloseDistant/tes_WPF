using System.Windows.Controls;

namespace RuinaoHardwareDebugWpf.Views;

/// <summary>
/// 仿真页面视图（占位）。
/// XAML 负责布局和样式，这个 Code-Behind 文件只调用 InitializeComponent，逻辑交给 SimViewModel。
/// </summary>
public partial class SimView : UserControl
{
    public SimView()
    {
        InitializeComponent();
    }
}
