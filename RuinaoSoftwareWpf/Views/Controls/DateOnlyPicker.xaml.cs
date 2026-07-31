namespace RuinaoSoftwareWpf.Views.Controls;

using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

public partial class DateOnlyPicker : UserControl
{
    public static readonly DependencyProperty SelectedDateProperty = DependencyProperty.Register(
        nameof(SelectedDate),
        typeof(DateOnly),
        typeof(DateOnlyPicker),
        new FrameworkPropertyMetadata(
            default(DateOnly),
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            OnSelectedDateChanged));

    public static readonly DependencyProperty SelectedDateTextProperty = DependencyProperty.Register(
        nameof(SelectedDateText),
        typeof(string),
        typeof(DateOnlyPicker),
        new FrameworkPropertyMetadata(
            string.Empty,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            OnSelectedDateTextChanged));

    public static readonly DependencyProperty PlaceholderTextProperty = DependencyProperty.Register(
        nameof(PlaceholderText),
        typeof(string),
        typeof(DateOnlyPicker),
        new PropertyMetadata("请选择", OnDisplayPropertyChanged));

    public static readonly DependencyProperty ActionTextProperty = DependencyProperty.Register(
        nameof(ActionText),
        typeof(string),
        typeof(DateOnlyPicker),
        new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty IsInvalidProperty = DependencyProperty.Register(
        nameof(IsInvalid),
        typeof(bool),
        typeof(DateOnlyPicker),
        new PropertyMetadata(false));

    public static readonly DependencyProperty RestrictToTodayProperty = DependencyProperty.Register(
        nameof(RestrictToToday),
        typeof(bool),
        typeof(DateOnlyPicker),
        new PropertyMetadata(false));

    private static readonly Brush MutedBrush = new SolidColorBrush(Color.FromRgb(111, 122, 140));
    private static readonly Brush SelectedBorderBrush = new SolidColorBrush(Color.FromRgb(217, 155, 54));
    private static readonly Brush SelectedBackgroundBrush = new SolidColorBrush(Color.FromRgb(58, 46, 29));
    private static readonly Brush CurrentBackgroundBrush = new SolidColorBrush(Color.FromRgb(39, 46, 59));
    private DateOnly displayedMonth = FirstDayOfMonth(DateOnly.FromDateTime(DateTime.Today));
    private CalendarView currentView = CalendarView.Day;
    private int yearPageStart;
    private bool synchronizingDate;

    public DateOnlyPicker()
    {
        InitializeComponent();
        yearPageStart = GetYearPageStart(displayedMonth.Year);
        UpdateDateText();
    }

    public DateOnly SelectedDate
    {
        get => (DateOnly)GetValue(SelectedDateProperty);
        set => SetValue(SelectedDateProperty, value);
    }

    public string SelectedDateText
    {
        get => (string)GetValue(SelectedDateTextProperty);
        set => SetValue(SelectedDateTextProperty, value);
    }

    public string PlaceholderText
    {
        get => (string)GetValue(PlaceholderTextProperty);
        set => SetValue(PlaceholderTextProperty, value);
    }

    public string ActionText
    {
        get => (string)GetValue(ActionTextProperty);
        set => SetValue(ActionTextProperty, value);
    }

    public bool IsInvalid
    {
        get => (bool)GetValue(IsInvalidProperty);
        set => SetValue(IsInvalidProperty, value);
    }

    public bool RestrictToToday
    {
        get => (bool)GetValue(RestrictToTodayProperty);
        set => SetValue(RestrictToTodayProperty, value);
    }

    private static void OnSelectedDateChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs eventArgs)
    {
        var picker = (DateOnlyPicker)dependencyObject;
        if (picker.synchronizingDate)
        {
            return;
        }

        var date = (DateOnly)eventArgs.NewValue;
        picker.synchronizingDate = true;
        picker.SetCurrentValue(
            SelectedDateTextProperty,
            date == default ? string.Empty : date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        picker.synchronizingDate = false;
        if (date != default)
        {
            picker.displayedMonth = FirstDayOfMonth(date);
        }

        picker.UpdateDateText();
    }

    private static void OnSelectedDateTextChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs eventArgs)
    {
        var picker = (DateOnlyPicker)dependencyObject;
        if (picker.synchronizingDate)
        {
            picker.UpdateDateText();
            return;
        }

        var text = (eventArgs.NewValue as string)?.Trim() ?? string.Empty;
        var date = DateOnly.TryParseExact(
            text,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsed)
            ? parsed
            : default;
        picker.synchronizingDate = true;
        picker.SetCurrentValue(SelectedDateProperty, date);
        picker.synchronizingDate = false;
        if (date != default)
        {
            picker.displayedMonth = FirstDayOfMonth(date);
        }

        picker.UpdateDateText();
    }

    private static void OnDisplayPropertyChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs eventArgs)
    {
        ((DateOnlyPicker)dependencyObject).UpdateDateText();
    }

    private void Field_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        displayedMonth = FirstDayOfMonth(SelectedDate == default
            ? DateOnly.FromDateTime(DateTime.Today)
            : SelectedDate);
        currentView = CalendarView.Day;
        yearPageStart = GetYearPageStart(displayedMonth.Year);
        RenderCurrentView();
        CalendarPopup.IsOpen = true;
    }

    private void PreviousPeriodButton_Click(object sender, RoutedEventArgs e)
    {
        switch (currentView)
        {
            case CalendarView.Day when displayedMonth > new DateOnly(1900, 1, 1):
                displayedMonth = displayedMonth.AddMonths(-1);
                break;
            case CalendarView.Month when displayedMonth.Year > 1900:
                displayedMonth = ChangeYear(displayedMonth, displayedMonth.Year - 1);
                break;
            case CalendarView.Year when yearPageStart > 1889:
                yearPageStart -= 10;
                break;
        }

        RenderCurrentView();
    }

    private void NextPeriodButton_Click(object sender, RoutedEventArgs e)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        switch (currentView)
        {
            case CalendarView.Day when displayedMonth < new DateOnly(9999, 12, 1)
                && (!RestrictToToday || displayedMonth < FirstDayOfMonth(today)):
                displayedMonth = displayedMonth.AddMonths(1);
                break;
            case CalendarView.Month when displayedMonth.Year < 9999
                && (!RestrictToToday || displayedMonth.Year < today.Year):
                displayedMonth = ChangeYear(displayedMonth, displayedMonth.Year + 1);
                break;
            case CalendarView.Year when yearPageStart + 10 <= 9999
                && (!RestrictToToday || yearPageStart + 10 <= today.Year):
                yearPageStart += 10;
                break;
        }

        RenderCurrentView();
    }

    private void CalendarTitleButton_Click(object sender, RoutedEventArgs e)
    {
        currentView = currentView switch
        {
            CalendarView.Day => CalendarView.Month,
            CalendarView.Month => CalendarView.Year,
            _ => CalendarView.Year
        };
        yearPageStart = GetYearPageStart(displayedMonth.Year);
        RenderCurrentView();
    }

    private void CalendarDayButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: DateOnly date } || !IsAllowedDate(date))
        {
            return;
        }

        SelectedDate = date;
        CalendarPopup.IsOpen = false;
    }

    private void MonthButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: int month })
        {
            return;
        }

        displayedMonth = new DateOnly(displayedMonth.Year, month, 1);
        currentView = CalendarView.Day;
        RenderCurrentView();
    }

    private void YearButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: int year } || !IsAllowedYear(year))
        {
            return;
        }

        displayedMonth = ChangeYear(displayedMonth, year);
        currentView = CalendarView.Month;
        RenderCurrentView();
    }

    private void RenderCurrentView()
    {
        DayViewPanel.Visibility = currentView == CalendarView.Day ? Visibility.Visible : Visibility.Collapsed;
        MonthChoicesGrid.Visibility = currentView == CalendarView.Month ? Visibility.Visible : Visibility.Collapsed;
        YearChoicesGrid.Visibility = currentView == CalendarView.Year ? Visibility.Visible : Visibility.Collapsed;

        switch (currentView)
        {
            case CalendarView.Day:
                RenderCalendarDays();
                break;
            case CalendarView.Month:
                RenderMonthChoices();
                break;
            case CalendarView.Year:
                RenderYearChoices();
                break;
        }
    }

    private void RenderCalendarDays()
    {
        CalendarTitleButton.Content = displayedMonth.ToString("yyyy 年 MM 月  ▾", CultureInfo.InvariantCulture);
        CalendarDaysGrid.Children.Clear();
        var firstDayOffset = ((int)displayedMonth.DayOfWeek + 6) % 7;
        var gridStartDate = displayedMonth.AddDays(-firstDayOffset);
        var today = DateOnly.FromDateTime(DateTime.Today);

        for (var index = 0; index < 42; index++)
        {
            var date = gridStartDate.AddDays(index);
            var button = CreateChoiceButton(date.Day.ToString(CultureInfo.InvariantCulture), date, "DateDayButton");
            button.Click += CalendarDayButton_Click;
            if (date.Month != displayedMonth.Month)
            {
                button.Foreground = MutedBrush;
                button.Opacity = 0.45;
            }

            if (!IsAllowedDate(date))
            {
                button.IsEnabled = false;
                button.Opacity = 0.5;
            }
            else if (date == today)
            {
                button.BorderBrush = new SolidColorBrush(Color.FromRgb(75, 86, 105));
                button.Background = CurrentBackgroundBrush;
            }

            if (date == SelectedDate)
            {
                ApplySelectedStyle(button);
            }

            CalendarDaysGrid.Children.Add(button);
        }
    }

    private void RenderMonthChoices()
    {
        CalendarTitleButton.Content = $"{displayedMonth.Year} 年  ▾";
        MonthChoicesGrid.Children.Clear();
        var today = DateOnly.FromDateTime(DateTime.Today);
        for (var month = 1; month <= 12; month++)
        {
            var button = CreateChoiceButton($"{month} 月", month, "DateChoiceButton");
            button.Click += MonthButton_Click;
            if (RestrictToToday && displayedMonth.Year == today.Year && month > today.Month)
            {
                button.IsEnabled = false;
                button.Opacity = 0.5;
            }
            else if (month == displayedMonth.Month)
            {
                ApplySelectedStyle(button);
            }

            MonthChoicesGrid.Children.Add(button);
        }
    }

    private void RenderYearChoices()
    {
        CalendarTitleButton.Content = $"{yearPageStart + 1} - {yearPageStart + 10}";
        YearChoicesGrid.Children.Clear();
        for (var index = 0; index < 12; index++)
        {
            var year = yearPageStart + index;
            var button = CreateChoiceButton(year.ToString(CultureInfo.InvariantCulture), year, "DateChoiceButton");
            button.Click += YearButton_Click;
            if (index is 0 or 11)
            {
                button.Foreground = MutedBrush;
                button.Opacity = 0.55;
            }

            if (!IsAllowedYear(year))
            {
                button.IsEnabled = false;
                button.Opacity = 0.5;
            }
            else if (year == displayedMonth.Year)
            {
                ApplySelectedStyle(button);
            }

            YearChoicesGrid.Children.Add(button);
        }
    }

    private Button CreateChoiceButton(string content, object tag, string styleKey) => new()
    {
        Content = content,
        Tag = tag,
        Style = (Style)FindResource(styleKey)
    };

    private static void ApplySelectedStyle(Button button)
    {
        button.BorderBrush = SelectedBorderBrush;
        button.Background = SelectedBackgroundBrush;
        button.FontWeight = FontWeights.SemiBold;
        button.Opacity = 1;
    }

    private void UpdateDateText()
    {
        if (DateText is null)
        {
            return;
        }

        DateText.Text = SelectedDate == default
            ? PlaceholderText
            : SelectedDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        DateText.Foreground = SelectedDate == default ? MutedBrush : Brushes.WhiteSmoke;
    }

    private bool IsAllowedDate(DateOnly date)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        return date.Year >= 1900 && (!RestrictToToday || date <= today);
    }

    private bool IsAllowedYear(int year) =>
        year >= 1900 && (!RestrictToToday || year <= DateTime.Today.Year);

    private DateOnly ChangeYear(DateOnly date, int year)
    {
        year = Math.Clamp(year, 1900, RestrictToToday ? DateTime.Today.Year : 9999);
        var month = date.Month;
        if (RestrictToToday && year == DateTime.Today.Year)
        {
            month = Math.Min(month, DateTime.Today.Month);
        }

        return new DateOnly(year, month, 1);
    }

    private static int GetYearPageStart(int year) => year / 10 * 10 - 1;

    private static DateOnly FirstDayOfMonth(DateOnly date) => new(date.Year, date.Month, 1);

    private enum CalendarView
    {
        Day,
        Month,
        Year
    }
}
