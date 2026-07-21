namespace RuinaoSoftwareWpf.Views;

using System.Windows;
using System.Windows.Controls;

/// <summary>
/// 报告页面视图。
/// XAML 负责布局和样式，这个 Code-Behind 文件只做加载时的数据刷新。
/// </summary>
public partial class ReportView : UserControl
{
    public ReportView()
    {
        InitializeComponent();
    }

    private async void UserControl_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is ReportViewModel viewModel)
        {
            await viewModel.InitializeAsync();
        }
    }

}
