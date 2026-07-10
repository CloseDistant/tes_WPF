using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace RuinaoHardwareDebugWpf.Views;

public partial class EegMarkerSettingsDialog : Window
{
    private readonly List<EegMarkerTag> markerTags;
    private readonly IReadOnlyList<MarkerColorOption> colorOptions =
    [
        new("红色", Color.FromRgb(181, 61, 63)),
        new("金色", Color.FromRgb(174, 128, 45)),
        new("绿色", Color.FromRgb(61, 156, 85)),
        new("灰蓝", Color.FromRgb(92, 100, 118)),
        new("紫色", Color.FromRgb(89, 50, 180)),
        new("青色", Color.FromRgb(45, 160, 190))
    ];

    public EegMarkerSettingsDialog(IReadOnlyList<EegMarkerTag> currentTags)
    {
        InitializeComponent();
        markerTags = currentTags.ToList();
        BuildColorOptions();
        RenderTagList();
    }

    public IReadOnlyList<EegMarkerTag> MarkerTags => markerTags.ToArray();

    private void BuildColorOptions()
    {
        ColorComboBox.Items.Clear();
        foreach (var option in colorOptions)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new Border
            {
                Width = 42,
                Height = 12,
                Background = new SolidColorBrush(option.Color),
                Margin = new Thickness(0, 0, 10, 0)
            });
            row.Children.Add(new TextBlock
            {
                Text = option.Name,
                Foreground = (Brush)FindResource("Text"),
                VerticalAlignment = VerticalAlignment.Center
            });

            ColorComboBox.Items.Add(new ComboBoxItem
            {
                Content = row,
                Tag = option,
                Style = (Style)FindResource("DarkComboBoxItem")
            });
        }

        ColorComboBox.SelectedIndex = 0;
    }

    private void RenderTagList()
    {
        TagListPanel.Children.Clear();
        foreach (var tag in markerTags)
        {
            var row = new Grid
            {
                Height = 34,
                Margin = new Thickness(6, 4, 6, 0),
                Background = new SolidColorBrush(Color.FromRgb(32, 39, 55))
            };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });

            row.Children.Add(CreateCellText(tag.Name, 0));
            var swatch = new Border
            {
                Width = 42,
                Height = 14,
                Background = new SolidColorBrush(tag.Color),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(swatch, 1);
            row.Children.Add(swatch);
            row.Children.Add(CreateCellText(tag.KeyText, 2));

            var deleteButton = new Button
            {
                Content = "×",
                Width = 24,
                Height = 24,
                Background = Brushes.Transparent,
                Foreground = (Brush)FindResource("SubText"),
                BorderThickness = new Thickness(0),
                Tag = tag
            };
            deleteButton.Click += DeleteButton_Click;
            Grid.SetColumn(deleteButton, 3);
            row.Children.Add(deleteButton);

            TagListPanel.Children.Add(row);
        }
    }

    private TextBlock CreateCellText(string text, int column)
    {
        var block = new TextBlock
        {
            Text = text,
            Foreground = (Brush)FindResource("Text"),
            FontSize = 14,
            Margin = new Thickness(12, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(block, column);
        return block;
    }

    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;
        var name = NameTextBox.Text.Trim();
        var shortcut = ShortcutTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            ErrorText.Text = "请输入标签名称";
            return;
        }

        if (string.IsNullOrWhiteSpace(shortcut))
        {
            ErrorText.Text = "请键入快捷键";
            return;
        }

        if (markerTags.Any(item => string.Equals(item.KeyText, shortcut, StringComparison.OrdinalIgnoreCase)))
        {
            ErrorText.Text = "快捷键已占用";
            return;
        }

        var color = GetSelectedColor();
        markerTags.Add(new EegMarkerTag(name, shortcut, color));
        NameTextBox.Clear();
        ShortcutTextBox.Clear();
        RenderTagList();
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not EegMarkerTag tag)
        {
            return;
        }

        markerTags.Remove(tag);
        RenderTagList();
    }

    private void ShortcutTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Tab or Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt)
        {
            return;
        }

        ShortcutTextBox.Text = NormalizeKey(e.Key);
        e.Handled = true;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
        }
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
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

    private Color GetSelectedColor()
    {
        return (ColorComboBox.SelectedItem as ComboBoxItem)?.Tag is MarkerColorOption option
            ? option.Color
            : colorOptions[0].Color;
    }

    private static string NormalizeKey(Key key)
    {
        return key switch
        {
            Key.Space => "Space",
            >= Key.D0 and <= Key.D9 => key.ToString(),
            >= Key.NumPad0 and <= Key.NumPad9 => key.ToString(),
            _ => key.ToString()
        };
    }

    private sealed record MarkerColorOption(string Name, Color Color);
}
