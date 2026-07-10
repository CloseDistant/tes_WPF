using System.Windows.Controls;

namespace RuinaoHardwareDebugWpf.Views;

/// <summary>
/// 总览监控页面视图。
/// XAML 负责布局和样式，这个 Code-Behind 文件只调用 InitializeComponent，逻辑交给 MonitorViewModel。
/// </summary>
public partial class MonitorView : UserControl
{
    public MonitorView()
    {
        InitializeComponent();
    }
}
