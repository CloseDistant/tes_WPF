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
        if (e.PropertyName == nameof(PatientFormViewModel.Name))
        {
            UpdateFieldValidation(NameBox, PatientSaveRequestValidator.NameField);
        }

        if (e.PropertyName == nameof(PatientFormViewModel.BirthDateText) && !string.IsNullOrWhiteSpace(viewModel.BirthDateText))
        {
            BirthDatePicker.IsInvalid = false;
        }

        if (e.PropertyName == nameof(PatientFormViewModel.ClinicalInfo) && viewModel.IsCreateMode)
        {
            UpdateFieldValidation(ClinicalInfoBox, PatientSaveRequestValidator.ClinicalInfoField);
        }

        if (e.PropertyName is nameof(PatientFormViewModel.EmergencyContactName) or nameof(PatientFormViewModel.EmergencyContactPhone))
        {
            UpdateEmergencyPlaceholders();
        }

        if (!viewModel.IsEditMode)
        {
            return;
        }

        switch (e.PropertyName)
        {
            case nameof(PatientFormViewModel.IdCardNumber):
                UpdateFieldValidation(IdCardNumberBox, PatientSaveRequestValidator.IdCardNumberField);
                break;
            case nameof(PatientFormViewModel.Phone):
                UpdateFieldValidation(PhoneBox, PatientSaveRequestValidator.PhoneField);
                break;
            case nameof(PatientFormViewModel.EmergencyContactName):
                UpdateFieldValidation(EmergencyNameBox, PatientSaveRequestValidator.EmergencyContactNameField);
                break;
            case nameof(PatientFormViewModel.EmergencyContactPhone):
                UpdateFieldValidation(EmergencyPhoneBox, PatientSaveRequestValidator.EmergencyContactPhoneField);
                break;
            case nameof(PatientFormViewModel.HomeAddress):
                UpdateFieldValidation(HomeAddressBox, PatientSaveRequestValidator.HomeAddressField);
                break;
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
        if (e.Handled && sender is TextBox textBox)
        {
            Highlight(textBox);
            viewModel.ShowError(sender == EmergencyPhoneBox
                ? "紧急联系人电话只能填写数字"
                : "联系电话只能填写数字");
        }
    }

    private void PhoneBox_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        if (sender is not TextBox textBox
            || !TryGetPastedText(e, out var text)
            || !PatientSaveRequestValidator.ContainsOnlyAsciiDigits(text))
        {
            e.CancelCommand();
            if (sender is TextBox invalidTextBox)
            {
                Highlight(invalidTextBox);
            }
            viewModel.ShowError(sender == EmergencyPhoneBox
                ? "紧急联系人电话只能填写数字"
                : "联系电话只能填写数字");
            return;
        }

        if (WouldExceedMaxLength(textBox, text))
        {
            e.CancelCommand();
            Highlight(textBox);
            viewModel.ShowError(sender == EmergencyPhoneBox
                ? $"紧急联系人电话不能超过 {PatientSaveRequestValidator.PhoneMaxLength} 位"
                : $"联系电话不能超过 {PatientSaveRequestValidator.PhoneMaxLength} 位");
        }
    }

    private void LimitedTextBox_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        if (sender is not TextBox textBox || !TryGetPastedText(e, out var text))
        {
            e.CancelCommand();
            return;
        }

        var allowLineBreaks = sender == ClinicalInfoBox;
        if (PatientSaveRequestValidator.ContainsDisallowedControlCharacters(text, allowLineBreaks))
        {
            e.CancelCommand();
            Highlight(textBox);
            viewModel.ShowError(sender == ClinicalInfoBox
                ? "临床信息包含不支持的控制字符"
                : "输入内容包含不支持的控制字符");
            return;
        }

        if (WouldExceedMaxLength(textBox, text))
        {
            e.CancelCommand();
            Highlight(textBox);
            viewModel.ShowError(GetLengthErrorMessage(textBox));
        }
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        ClearHighlight(NameBox);
        BirthDatePicker.IsInvalid = false;
        ClearHighlight(IdCardNumberBox);
        ClearHighlight(PhoneBox);
        ClearHighlight(EmergencyNameBox);
        ClearHighlight(EmergencyPhoneBox);
        ClearHighlight(HomeAddressBox);
        ClearHighlight(ClinicalInfoBox);
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

            if (viewModel.IsEditMode && validation.HasError(PatientSaveRequestValidator.IdCardNumberField))
            {
                Highlight(IdCardNumberBox);
            }

            if (viewModel.IsEditMode && validation.HasError(PatientSaveRequestValidator.EmergencyContactNameField))
            {
                Highlight(EmergencyNameBox);
            }

            if (viewModel.IsEditMode && validation.HasError(PatientSaveRequestValidator.EmergencyContactPhoneField))
            {
                Highlight(EmergencyPhoneBox);
            }

            if (viewModel.IsEditMode && validation.HasError(PatientSaveRequestValidator.HomeAddressField))
            {
                Highlight(HomeAddressBox);
            }

            if (viewModel.IsCreateMode && validation.HasError(PatientSaveRequestValidator.ClinicalInfoField))
            {
                Highlight(ClinicalInfoBox);
            }

            FocusFirstInvalidField(validation);

            return;
        }

        Request = request;
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Highlight(TextBox textBox) => textBox.Style = (Style)FindResource("PatientErrorFieldBox");

    private void ClearHighlight(TextBox textBox) => textBox.Style = (Style)FindResource("PatientFieldBox");

    private void UpdateFieldValidation(TextBox textBox, string fieldName)
    {
        var error = viewModel.ValidateField(fieldName);
        if (error is null)
        {
            ClearHighlight(textBox);
            viewModel.ClearError();
            return;
        }

        Highlight(textBox);
        viewModel.ShowError(error.Message);
    }

    private void FocusFirstInvalidField(PatientValidationResult validation)
    {
        foreach (var error in validation.Errors)
        {
            switch (error.FieldName)
            {
                case PatientSaveRequestValidator.NameField:
                    NameBox.Focus();
                    return;
                case PatientSaveRequestValidator.BirthDateField:
                    BirthDatePicker.Focus();
                    return;
                case PatientSaveRequestValidator.IdCardNumberField:
                    IdCardNumberBox.Focus();
                    return;
                case PatientSaveRequestValidator.PhoneField:
                    PhoneBox.Focus();
                    return;
                case PatientSaveRequestValidator.EmergencyContactNameField:
                    EmergencyNameBox.Focus();
                    return;
                case PatientSaveRequestValidator.EmergencyContactPhoneField:
                    EmergencyPhoneBox.Focus();
                    return;
                case PatientSaveRequestValidator.HomeAddressField:
                    HomeAddressBox.Focus();
                    return;
                case PatientSaveRequestValidator.ClinicalInfoField:
                    ClinicalInfoBox.Focus();
                    return;
            }
        }
    }

    private static bool TryGetPastedText(DataObjectPastingEventArgs e, out string text)
    {
        if (e.SourceDataObject.GetDataPresent(DataFormats.UnicodeText)
            && e.SourceDataObject.GetData(DataFormats.UnicodeText) is string pastedText)
        {
            text = pastedText;
            return true;
        }

        text = string.Empty;
        return false;
    }

    private static bool WouldExceedMaxLength(TextBox textBox, string pastedText)
    {
        return textBox.MaxLength > 0
            && textBox.Text.Length - textBox.SelectionLength + pastedText.Length > textBox.MaxLength;
    }

    private static string GetLengthErrorMessage(TextBox textBox)
    {
        if (textBox == null)
        {
            return "输入内容超过长度限制";
        }

        return textBox.Name switch
        {
            nameof(NameBox) => $"姓名不能超过 {PatientSaveRequestValidator.PatientNameMaxLength} 个字符",
            nameof(IdCardNumberBox) => $"身份证号不能超过 {PatientSaveRequestValidator.IdCardNumberMaxLength} 个字符",
            nameof(EmergencyNameBox) => $"紧急联系人姓名不能超过 {PatientSaveRequestValidator.EmergencyContactNameMaxLength} 个字符",
            nameof(HomeAddressBox) => $"家庭住址不能超过 {PatientSaveRequestValidator.HomeAddressMaxLength} 个字符",
            nameof(ClinicalInfoBox) => $"临床信息不能超过 {PatientSaveRequestValidator.ClinicalInfoMaxLength} 个字符",
            _ => "输入内容超过长度限制"
        };
    }

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
