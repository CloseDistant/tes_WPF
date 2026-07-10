namespace RuinaoHardwareDebugWpf.Views.Controls;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

public partial class AssessmentQuestionnaireControl : UserControl
{
    public AssessmentQuestionnaireControl()
    {
        InitializeComponent();
        IsVisibleChanged += OnIsVisibleChanged;
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
        {
            Dispatcher.BeginInvoke(ResetScroll, DispatcherPriority.Loaded);
        }
    }

    private void ResetScroll()
    {
        QuestionnaireScrollViewer.ScrollToTop();
        QuestionnaireScrollViewer.ScrollToLeftEnd();
    }
}
