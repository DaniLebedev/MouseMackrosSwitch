using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MouseMackros;

public partial class ColorPickerDialog : Window
{
    private static readonly string[] Palette =
    {
        "#FF5A5A", "#F2A33C", "#FFD84C", "#3FB950", "#39B7C4", "#4C9AFF",
        "#9B6BF2", "#E06BB4", "#FF80FF", "#8000FF", "#C3CAD8", "#FFFFFF"
    };

    public ColorPickerDialog(string? currentColor)
    {
        InitializeComponent();

        ColorBox.Text = (currentColor ?? "").Trim().TrimStart('#');
        ColorBox.TextChanged += (_, _) => UpdatePreview();

        foreach (var color in Palette)
        {
            var brush = BrushOf(color) ?? Brushes.Transparent;
            var button = new Button
            {
                Width = 42,
                Height = 28,
                Margin = new Thickness(0, 0, 8, 8),
                Background = brush,
                BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x31, 0x38, 0x46)),
                BorderThickness = new Thickness(1),
                Tag = color,
                ToolTip = color
            };
            button.Click += (_, _) =>
            {
                ColorBox.Text = color.TrimStart('#');
                UpdatePreview();
            };
            PalettePanel.Children.Add(button);
        }

        UpdatePreview();
    }

    public string? SelectedColor { get; private set; }

    public bool Saved { get; private set; }

    private void UpdatePreview()
    {
        Preview.Background = BrushOf(ColorBox.Text) ?? Brushes.Transparent;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!TryColor(ColorBox.Text, out var color))
        {
            AppDialog.ShowError("Некорректный цвет. Нужен RRGGBB или #RRGGBB.", "Цвет колеса");
            return;
        }

        SelectedColor = color;
        Saved = true;
        DialogResult = true;
        Close();
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        SelectedColor = null;
        Saved = true;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private static bool TryColor(string? text, out string color)
    {
        var value = (text ?? "").Trim().TrimStart('#');
        if (value.Length == 6 &&
            int.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _))
        {
            color = "#" + value.ToUpperInvariant();
            return true;
        }

        color = "";
        return false;
    }

    private static Brush? BrushOf(string? text)
    {
        if (!TryColor(text, out var color)) return null;
        return new SolidColorBrush((System.Windows.Media.Color)
            System.Windows.Media.ColorConverter.ConvertFromString(color));
    }
}
