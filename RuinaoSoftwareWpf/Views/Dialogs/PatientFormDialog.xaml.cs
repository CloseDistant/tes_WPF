namespace RuinaoSoftwareWpf.Views.Dialogs;

using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

public partial class PatientFormDialog : Window
{
    private readonly PatientFormViewModel viewModel;

    public PatientFormDialog(PatientRecord? patient)
    {
        InitializeComponent();
        viewModel = new PatientFormViewModel(patient);
        DataContext = viewModel;
        Height = viewModel.IsCreateMode ? 430 : 500;
        viewModel.PropertyChanged += ViewModel_PropertyChanged;
        UpdateEmergencyPlaceholders();
    }

    public PatientSaveRequest? Request { get; private set; }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PatientFormViewModel.Name)
            && !string.IsNullOrWhiteSpace(viewModel.Name)
            && viewModel.Name.Trim().Length <= PatientSaveRequestValidator.PatientNameMaxLength)
        {
            ClearHighlight(NameBox);
        }

        if (e.PropertyName == nameof(PatientFormViewModel.BirthDateText) && !string.IsNullOrWhiteSpace(viewModel.BirthDateText))
        {
            BirthDatePicker.IsInvalid = false;
        }

        if (e.PropertyName == nameof(PatientFormViewModel.Phone)
            && !string.IsNullOrWhiteSpace(viewModel.Phone)
            && PatientSaveRequestValidator.ContainsOnlyAsciiDigits(viewModel.Phone))
        {
            ClearHighlight(PhoneBox);
        }

        if (e.PropertyName is nameof(PatientFormViewModel.EmergencyContactName) or nameof(PatientFormViewModel.EmergencyContactPhone))
        {
            UpdateEmergencyPlaceholders();
        }

        if (e.PropertyName == nameof(PatientFormViewModel.EmergencyContactPhone)
            && (string.IsNullOrWhiteSpace(viewModel.EmergencyContactPhone)
                || PatientSaveRequestValidator.ContainsOnlyAsciiDigits(viewModel.EmergencyContactPhone)))
        {
            ClearHighlight(EmergencyPhoneBox);
        }
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void EmergencyPlaceholder_FocusChanged(object sender, RoutedEventArgs e) => UpdateEmergencyPlaceholders();

    private void PhoneBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = !PatientSaveRequestValidator.ContainsOnlyAsciiDigits(e.Text);
    }

    private void PhoneBox_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        if (!e.SourceDataObject.GetDataPresent(DataFormats.UnicodeText)
            || e.SourceDataObject.GetData(DataFormats.UnicodeText) is not string text
            || !PatientSaveRequestValidator.ContainsOnlyAsciiDigits(text))
        {
            e.CancelCommand();
        }
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        ClearHighlight(NameBox);
        BirthDatePicker.IsInvalid = false;
        ClearHighlight(PhoneBox);
        ClearHighlight(EmergencyPhoneBox);
        var validation = viewModel.Validate(out var request);
        if (!validation.IsValid)
        {
            if (validation.HasError(PatientSaveRequestValidator.NameField))
            {
                Highlight(NameBox);
            }

            if (validation.HasError(PatientSaveRequestValidator.BirthDateField))
            {
                BirthDatePicker.IsInvalid = true;
            }

            if (viewModel.IsEditMode && validation.HasError(PatientSaveRequestValidator.PhoneField))
            {
                Highlight(PhoneBox);
            }

            if (viewModel.IsEditMode && validation.HasError(PatientSaveRequestValidator.EmergencyContactPhoneField))
            {
                Highlight(EmergencyPhoneBox);
            }

            return;
        }

        Request = request;
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Highlight(TextBox textBox) => textBox.Style = (Style)FindResource("PatientErrorFieldBox");

    private void ClearHighlight(TextBox textBox) => textBox.Style = (Style)FindResource("PatientFieldBox");

    private void UpdateEmergencyPlaceholders()
    {
        EmergencyNamePlaceholder.Visibility = string.IsNullOrWhiteSpace(viewModel.EmergencyContactName) && !EmergencyNameBox.IsKeyboardFocusWithin
            ? Visibility.Visible
            : Visibility.Collapsed;
        EmergencyPhonePlaceholder.Visibility = string.IsNullOrWhiteSpace(viewModel.EmergencyContactPhone) && !EmergencyPhoneBox.IsKeyboardFocusWithin
            ? Visibility.Visible
            : Visibility.Collapsed;
    }
}
