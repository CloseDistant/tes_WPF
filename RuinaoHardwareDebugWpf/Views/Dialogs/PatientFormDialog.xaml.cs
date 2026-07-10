namespace RuinaoHardwareDebugWpf.Views.Dialogs;

using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

public partial class PatientFormDialog : Window
{
    private readonly PatientFormViewModel viewModel;
    private DateOnly displayedCalendarMonth;

    public PatientFormDialog(PatientRecord? patient)
    {
        InitializeComponent();
        viewModel = new PatientFormViewModel(patient);
        DataContext = viewModel;
        Height = viewModel.IsCreateMode ? 430 : 500;
        displayedCalendarMonth = FirstDayOfMonth(patient?.BirthDate ?? DateOnly.FromDateTime(DateTime.Today));
        viewModel.PropertyChanged += ViewModel_PropertyChanged;
        UpdateEmergencyPlaceholders();
    }

    public PatientSaveRequest? Request { get; private set; }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PatientFormViewModel.Name) && !string.IsNullOrWhiteSpace(viewModel.Name))
        {
            ClearHighlight(NameBox);
        }

        if (e.PropertyName == nameof(PatientFormViewModel.BirthDateText) && !string.IsNullOrWhiteSpace(viewModel.BirthDateText))
        {
            ClearHighlight(BirthDateBox);
        }

        if (e.PropertyName == nameof(PatientFormViewModel.Phone) && !string.IsNullOrWhiteSpace(viewModel.Phone))
        {
            ClearHighlight(PhoneBox);
        }

        if (e.PropertyName is nameof(PatientFormViewModel.EmergencyContactName) or nameof(PatientFormViewModel.EmergencyContactPhone))
        {
            UpdateEmergencyPlaceholders();
        }
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void BirthDateBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        OpenBirthDatePicker();
        e.Handled = true;
    }

    private void BirthDateBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => OpenBirthDatePicker();

    private void PreviousMonthButton_Click(object sender, RoutedEventArgs e)
    {
        displayedCalendarMonth = displayedCalendarMonth.AddMonths(-1);
        RenderCalendarDays();
    }

    private void NextMonthButton_Click(object sender, RoutedEventArgs e)
    {
        displayedCalendarMonth = displayedCalendarMonth.AddMonths(1);
        RenderCalendarDays();
    }

    private void CalendarDayButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: DateOnly selectedDate })
        {
            return;
        }

        viewModel.BirthDateText = selectedDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        DatePickerPopup.IsOpen = false;
    }

    private void EmergencyPlaceholder_FocusChanged(object sender, RoutedEventArgs e) => UpdateEmergencyPlaceholders();

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        ClearHighlight(NameBox);
        ClearHighlight(BirthDateBox);
        ClearHighlight(PhoneBox);
        var validation = viewModel.Validate(out var request);
        if (!validation.IsValid)
        {
            if (validation.HasError(PatientSaveRequestValidator.NameField))
            {
                Highlight(NameBox);
            }

            if (validation.HasError(PatientSaveRequestValidator.BirthDateField))
            {
                Highlight(BirthDateBox);
            }

            if (viewModel.IsEditMode && validation.HasError(PatientSaveRequestValidator.PhoneField))
            {
                Highlight(PhoneBox);
            }

            return;
        }

        Request = request;
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Highlight(TextBox textBox) => textBox.Style = (Style)FindResource("PatientErrorFieldBox");

    private void ClearHighlight(TextBox textBox) => textBox.Style = (Style)FindResource("PatientFieldBox");

    private void OpenBirthDatePicker()
    {
        if (DateOnly.TryParseExact(viewModel.BirthDateText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var selectedDate))
        {
            displayedCalendarMonth = FirstDayOfMonth(selectedDate);
        }

        DatePickerPopup.PlacementTarget = BirthDateBox;
        DatePickerPopup.Placement = PlacementMode.Bottom;
        RenderCalendarDays();
        DatePickerPopup.IsOpen = true;
    }

    private void RenderCalendarDays()
    {
        CalendarTitleText.Text = displayedCalendarMonth.ToString("yyyy 年 MM 月", CultureInfo.InvariantCulture);
        CalendarDaysGrid.Children.Clear();
        var firstDayOffset = ((int)displayedCalendarMonth.DayOfWeek + 6) % 7;
        var gridStartDate = displayedCalendarMonth.AddDays(-firstDayOffset);
        var selectedDate = DateOnly.TryParseExact(viewModel.BirthDateText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : (DateOnly?)null;
        var today = DateOnly.FromDateTime(DateTime.Today);

        for (var index = 0; index < 42; index++)
        {
            var date = gridStartDate.AddDays(index);
            var button = new Button
            {
                Content = date.Day.ToString(CultureInfo.InvariantCulture),
                Tag = date,
                Style = (Style)FindResource("CalendarDayButton")
            };
            button.Click += CalendarDayButton_Click;
            if (date.Month != displayedCalendarMonth.Month)
            {
                button.Foreground = (Brush)FindResource("SubText");
                button.Opacity = 0.38;
            }

            if (date == today)
            {
                button.BorderBrush = (Brush)FindResource("Line");
                button.Background = new SolidColorBrush(Color.FromRgb(45, 51, 66));
            }

            if (selectedDate == date)
            {
                button.BorderBrush = (Brush)FindResource("Gold");
                button.Background = new SolidColorBrush(Color.FromRgb(58, 46, 29));
                button.FontWeight = FontWeights.SemiBold;
                button.Opacity = 1;
            }

            CalendarDaysGrid.Children.Add(button);
        }
    }

    private static DateOnly FirstDayOfMonth(DateOnly date) => new(date.Year, date.Month, 1);

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
