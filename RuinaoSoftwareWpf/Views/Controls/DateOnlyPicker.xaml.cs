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

    private DateOnly displayedMonth = FirstDayOfMonth(DateOnly.FromDateTime(DateTime.Today));

    public DateOnlyPicker()
    {
        InitializeComponent();
        UpdateDateText();
    }

    public DateOnly SelectedDate
    {
        get => (DateOnly)GetValue(SelectedDateProperty);
        set => SetValue(SelectedDateProperty, value);
    }

    private static void OnSelectedDateChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs eventArgs)
    {
        var picker = (DateOnlyPicker)dependencyObject;
        if ((DateOnly)eventArgs.NewValue != default)
        {
            picker.displayedMonth = FirstDayOfMonth((DateOnly)eventArgs.NewValue);
        }

        picker.UpdateDateText();
    }

    private void Field_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        displayedMonth = FirstDayOfMonth(SelectedDate == default
            ? DateOnly.FromDateTime(DateTime.Today)
            : SelectedDate);
        RenderCalendarDays();
        CalendarPopup.IsOpen = true;
    }

    private void PreviousMonthButton_Click(object sender, RoutedEventArgs e)
    {
        displayedMonth = displayedMonth.AddMonths(-1);
        RenderCalendarDays();
    }

    private void NextMonthButton_Click(object sender, RoutedEventArgs e)
    {
        displayedMonth = displayedMonth.AddMonths(1);
        RenderCalendarDays();
    }

    private void CalendarDayButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: DateOnly date })
        {
            return;
        }

        SelectedDate = date;
        CalendarPopup.IsOpen = false;
    }

    private void RenderCalendarDays()
    {
        CalendarTitleText.Text = displayedMonth.ToString("yyyy 年 MM 月", CultureInfo.InvariantCulture);
        CalendarDaysGrid.Children.Clear();
        var firstDayOffset = ((int)displayedMonth.DayOfWeek + 6) % 7;
        var gridStartDate = displayedMonth.AddDays(-firstDayOffset);
        var today = DateOnly.FromDateTime(DateTime.Today);

        for (var index = 0; index < 42; index++)
        {
            var date = gridStartDate.AddDays(index);
            var button = new Button
            {
                Content = date.Day.ToString(CultureInfo.InvariantCulture),
                Tag = date,
                Style = (Style)FindResource("DateDayButton")
            };
            button.Click += CalendarDayButton_Click;
            if (date.Month != displayedMonth.Month)
            {
                button.Foreground = new SolidColorBrush(Color.FromRgb(111, 122, 140));
                button.Opacity = 0.45;
            }

            if (date == today)
            {
                button.BorderBrush = new SolidColorBrush(Color.FromRgb(75, 86, 105));
                button.Background = new SolidColorBrush(Color.FromRgb(39, 46, 59));
            }

            if (date == SelectedDate)
            {
                button.BorderBrush = new SolidColorBrush(Color.FromRgb(217, 155, 54));
                button.Background = new SolidColorBrush(Color.FromRgb(58, 46, 29));
                button.FontWeight = FontWeights.SemiBold;
                button.Opacity = 1;
            }

            CalendarDaysGrid.Children.Add(button);
        }
    }

    private void UpdateDateText()
    {
        if (DateText is null)
        {
            return;
        }

        DateText.Text = SelectedDate == default
            ? "请选择"
            : SelectedDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static DateOnly FirstDayOfMonth(DateOnly date) => new(date.Year, date.Month, 1);
}
