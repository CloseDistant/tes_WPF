namespace RuinaoSoftwareWpf.Views.Dialogs;

using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

public partial class PatientSwitchDialog : Window
{
    private const int PageSize = 30;
    private readonly IPatientService patientService;
    private readonly string? currentPatientCode;
    private CancellationTokenSource? searchCancellation;
    private int nextOffset;
    private bool hasMore;
    private bool isLoading;

    public PatientSwitchDialog(
        IPatientService patientService,
        PageResult<PatientRecord> firstPage,
        string? currentPatientCode)
    {
        this.patientService = patientService;
        this.currentPatientCode = currentPatientCode;
        InitializeComponent();
        Items = [];
        DataContext = this;
        AppendPage(firstPage);
    }

    public ObservableCollection<PatientSwitchItem> Items { get; }

    public PatientRecord? SelectedPatient { get; private set; }

    private async Task ReloadAsync(CancellationToken cancellationToken)
    {
        while (isLoading)
        {
            await Task.Delay(20, cancellationToken);
        }

        Items.Clear();
        nextOffset = 0;
        hasMore = true;
        await LoadMoreAsync(cancellationToken);
    }

    private async Task LoadMoreAsync(CancellationToken cancellationToken = default)
    {
        if (isLoading || !hasMore)
        {
            return;
        }

        isLoading = true;
        try
        {
            var page = await patientService.GetPatientsPageAsync(
                new PageRequest(nextOffset, PageSize, SearchTextBox.Text),
                cancellationToken);
            AppendPage(page);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ErrorText.Text = $"读取患者失败：{exception.Message}";
        }
        finally
        {
            isLoading = false;
        }
    }

    private void AppendPage(PageResult<PatientRecord> page)
    {
        foreach (var patient in page.Items)
        {
            Items.Add(new PatientSwitchItem(patient, patient.PatientCode == currentPatientCode));
        }

        nextOffset += page.Items.Count;
        hasMore = page.HasMore;
        var current = Items.FirstOrDefault(item => item.PatientCode == currentPatientCode);
        PatientList.SelectedItem ??= current ?? Items.FirstOrDefault();
    }

    private async void SearchTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        searchCancellation?.Cancel();
        searchCancellation?.Dispose();
        searchCancellation = new CancellationTokenSource();
        try
        {
            await Task.Delay(250, searchCancellation.Token);
            await ReloadAsync(searchCancellation.Token);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async void PatientList_ScrollChanged(object sender, System.Windows.Controls.ScrollChangedEventArgs e)
    {
        if (e.VerticalChange == 0 || e.VerticalOffset < e.ExtentHeight - e.ViewportHeight - 2)
        {
            return;
        }

        await LoadMoreAsync();
    }

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
        searchCancellation?.Cancel();
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
