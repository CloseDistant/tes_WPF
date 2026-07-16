using System.Windows.Controls;

namespace RuinaoSoftwareWpf.Views;

/// <summary>
/// TI 控制页面视图。
/// 这是软件最核心的操作页面：左侧 TI 刺激组列表、右侧通道参数面板。
/// 逻辑由 TiControlViewModel 和 MainViewModel 提供，这里只负责加载 XAML。
/// </summary>
public partial class TiControlView : UserControl
{
    public TiControlView()
    {
        InitializeComponent();
    }
}
