using System.Windows.Controls;

namespace RuinaoHardwareDebugWpf.Views;

/// <summary>
/// 报告页面视图。
/// XAML 负责布局和样式，这个 Code-Behind 文件只调用 InitializeComponent，逻辑交给 ReportViewModel。
/// </summary>
public partial class ReportView : UserControl
{
    public ReportView()
    {
        InitializeComponent();
    }
}
