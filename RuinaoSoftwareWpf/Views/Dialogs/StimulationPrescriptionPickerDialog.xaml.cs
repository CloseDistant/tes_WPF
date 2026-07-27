using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace RuinaoSoftwareWpf.Views.Dialogs;

public partial class StimulationPrescriptionPickerDialog : Window
{
    public static readonly DependencyProperty StimulationTypeProperty = DependencyProperty.Register(
        nameof(StimulationType),
        typeof(string),
        typeof(StimulationPrescriptionPickerDialog),
        new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ApplyScopeTextProperty = DependencyProperty.Register(
        nameof(ApplyScopeText),
        typeof(string),
        typeof(StimulationPrescriptionPickerDialog),
        new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty SelectedPrescriptionProperty = DependencyProperty.Register(
        nameof(SelectedPrescription),
        typeof(PrescriptionDefinition),
        typeof(StimulationPrescriptionPickerDialog));

    public StimulationPrescriptionPickerDialog(
        string stimulationType,
        string applyScopeText,
        IEnumerable<PrescriptionDefinition> prescriptions)
    {
        InitializeComponent();
        StimulationType = stimulationType;
        ApplyScopeText = applyScopeText;
        Prescriptions = new ObservableCollection<PrescriptionDefinition>(prescriptions);
        if (Prescriptions.Count > 0)
        {
            SelectedPrescription = Prescriptions[0];
        }
    }

    public string StimulationType
    {
        get => (string)GetValue(StimulationTypeProperty);
        set => SetValue(StimulationTypeProperty, value);
    }

    public string ApplyScopeText
    {
        get => (string)GetValue(ApplyScopeTextProperty);
        set => SetValue(ApplyScopeTextProperty, value);
    }

    public PrescriptionDefinition? SelectedPrescription
    {
        get => (PrescriptionDefinition?)GetValue(SelectedPrescriptionProperty);
        set => SetValue(SelectedPrescriptionProperty, value);
    }

    public ObservableCollection<PrescriptionDefinition> Prescriptions { get; }

    private void PrescriptionSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ApplyButton.IsEnabled = SelectedPrescription is not null;
    }

    private void ApplyClick(object sender, RoutedEventArgs e)
    {
        if (SelectedPrescription is null)
        {
            return;
        }

        DialogResult = true;
    }

    private void CancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
