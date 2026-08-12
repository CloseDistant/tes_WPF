using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace RuinaoSoftwareWpf.Views;

public partial class EegParameterSettingsDialog : Window
{
    private static readonly int[] SampleRateOptions = [250, 500, 1000, 2000, 4000, 10000, 20000];
    private static readonly double[] HighPassOptions = [0.01, 0.05, 0.1, 0.3, 0.5, 1, 2, 5, 10, 20];
    private static readonly double[] LowPassOptions = [10, 15, 20, 30, 35, 40, 50, 70, 80, 100, 150, 200, 250];
    private static readonly double[] NotchOptions = [50, 60];
    private static readonly int[] GainOptions = [1, 2, 3, 4, 6, 8, 10, 12, 24];
    private static readonly string[] ReferenceOptions =
    [
        "双侧乳突",
        "鼻尖",
        "额正中(Cz)",
        "左侧乳突",
        "右侧乳突",
        "平均参考",
        "无参考"
    ];

    public EegParameterSettingsDialog(EegAcquisitionConfig config)
    {
        InitializeComponent();
        FillOptions();
        SelectedConfig = config;
        SelectInt(SampleRateComboBox, config.SampleRateHz);
        SelectNullableDouble(HighPassComboBox, config.HighPassHz);
        SelectNullableDouble(LowPassComboBox, config.LowPassHz);
        SelectNullableDouble(NotchComboBox, config.NotchHz);
        SelectInt(GainComboBox, config.HardwareGain);
        SelectString(ReferenceComboBox, config.ReferenceElectrode);
    }

    public EegAcquisitionConfig SelectedConfig { get; private set; }

    private void FillOptions()
    {
        foreach (var option in SampleRateOptions)
        {
            SampleRateComboBox.Items.Add(CreateOption($"{option} Hz", option));
        }

        foreach (var option in HighPassOptions)
        {
            HighPassComboBox.Items.Add(CreateOption($"{FormatNumber(option)} Hz", option));
        }

        HighPassComboBox.Items.Add(CreateOption("OFF", null));

        foreach (var option in LowPassOptions)
        {
            LowPassComboBox.Items.Add(CreateOption($"{FormatNumber(option)} Hz", option));
        }

        LowPassComboBox.Items.Add(CreateOption("OFF", null));

        foreach (var option in NotchOptions)
        {
            NotchComboBox.Items.Add(CreateOption($"{FormatNumber(option)} Hz", option));
        }

        NotchComboBox.Items.Add(CreateOption("OFF", null));

        foreach (var option in GainOptions)
        {
            GainComboBox.Items.Add(CreateOption($"×{option}", option));
        }

        foreach (var option in ReferenceOptions)
        {
            ReferenceComboBox.Items.Add(CreateOption(option, option));
        }
    }

    private static ComboBoxItem CreateOption(string content, object? tag)
    {
        return new ComboBoxItem { Content = content, Tag = tag };
    }

    private static void SelectInt(ComboBox comboBox, int value)
    {
        foreach (var item in comboBox.Items.OfType<ComboBoxItem>())
        {
            if (item.Tag is int intValue && intValue == value)
            {
                comboBox.SelectedItem = item;
                return;
            }
        }

        comboBox.SelectedIndex = 0;
    }

    private static void SelectNullableDouble(ComboBox comboBox, double? value)
    {
        foreach (var item in comboBox.Items.OfType<ComboBoxItem>())
        {
            if (item.Tag is null && value is null)
            {
                comboBox.SelectedItem = item;
                return;
            }

            if (item.Tag is double doubleValue &&
                value is not null &&
                Math.Abs(doubleValue - value.Value) < 0.0001)
            {
                comboBox.SelectedItem = item;
                return;
            }
        }

        comboBox.SelectedIndex = 0;
    }

    private static void SelectString(ComboBox comboBox, string value)
    {
        foreach (var item in comboBox.Items.OfType<ComboBoxItem>())
        {
            if (item.Tag is string text && string.Equals(text, value, StringComparison.Ordinal))
            {
                comboBox.SelectedItem = item;
                return;
            }
        }

        comboBox.SelectedIndex = 0;
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        var sampleRateHz = ReadInt(SampleRateComboBox, SelectedConfig.SampleRateHz);
        var highPassHz = ReadNullableDouble(HighPassComboBox);
        var lowPassHz = ReadNullableDouble(LowPassComboBox);
        var notchHz = ReadNullableDouble(NotchComboBox);
        var gain = ReadInt(GainComboBox, SelectedConfig.HardwareGain);
        var reference = ReadString(ReferenceComboBox, SelectedConfig.ReferenceElectrode);

        if (highPassHz is not null && lowPassHz is not null && highPassHz.Value >= lowPassHz.Value)
        {
            MessageBox.Show("高通不能大于低通", "参数设置", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (notchHz is not null && lowPassHz is not null && notchHz.Value >= lowPassHz.Value)
        {
            MessageBox.Show("陷波频率应小于低通截止频率，否则陷波无效", "参数设置", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var nyquistHz = sampleRateHz / 2.0;
        if (lowPassHz is not null && lowPassHz.Value > nyquistHz)
        {
            MessageBox.Show("低通滤波频率不得大于二分之一采样率", "参数设置", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        SelectedConfig = SelectedConfig with
        {
            ChannelCount = 64,
            SampleRateHz = sampleRateHz,
            HighPassHz = highPassHz,
            LowPassHz = lowPassHz,
            NotchHz = notchHz,
            HardwareGain = gain,
            ReferenceElectrode = reference
        };

        DialogResult = true;
    }

    private static int ReadInt(ComboBox comboBox, int fallback)
    {
        return comboBox.SelectedItem is ComboBoxItem { Tag: int value } ? value : fallback;
    }

    private static double? ReadNullableDouble(ComboBox comboBox)
    {
        if (comboBox.SelectedItem is not ComboBoxItem item || item.Tag is null)
        {
            return null;
        }

        return item.Tag is double value ? value : null;
    }

    private static string ReadString(ComboBox comboBox, string fallback)
    {
        return comboBox.SelectedItem is ComboBoxItem { Tag: string value } ? value : fallback;
    }

    private static string FormatNumber(double value)
    {
        return value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }
}
