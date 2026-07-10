namespace RuinaoHardwareDebugWpf.Views.Dialogs;

using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

public partial class PatientSwitchDialog : Window
{
    public PatientSwitchDialog(IReadOnlyList<PatientRecord> patients, string? currentPatientCode)
    {
        InitializeComponent();
        Items = new ObservableCollection<PatientSwitchItem>(
            patients.Select(item => new PatientSwitchItem(item, item.PatientCode == currentPatientCode)));
        DataContext = this;

        var current = Items.FirstOrDefault(item => item.PatientCode == currentPatientCode);
        PatientList.SelectedItem = current ?? Items.FirstOrDefault();
    }

    public ObservableCollection<PatientSwitchItem> Items { get; }

    public PatientRecord? SelectedPatient { get; private set; }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        ConfirmSelection();
    }

    private void PatientList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        ConfirmSelection();
    }

    private void ConfirmSelection()
    {
        ErrorText.Text = string.Empty;
        if (PatientList.SelectedItem is not PatientSwitchItem item)
        {
            ErrorText.Text = "请选择患者";
            return;
        }

        SelectedPatient = item.Source;
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}

public sealed class PatientSwitchItem
{
    public PatientSwitchItem(PatientRecord source, bool isCurrent)
    {
        Source = source;
        IsCurrent = isCurrent;
    }

    public PatientRecord Source { get; }
    public bool IsCurrent { get; }
    public string PatientCode => Source.PatientCode;
    public string Name => Source.Name;
    public string Sex => Source.Sex.ToDisplayText();
    public string Age => Source.Age.ToString();
    public Brush TextBrush => IsCurrent ? Brushes.White : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E4E8EF"));
    public Visibility CurrentMarkerVisibility => IsCurrent ? Visibility.Visible : Visibility.Collapsed;
}
