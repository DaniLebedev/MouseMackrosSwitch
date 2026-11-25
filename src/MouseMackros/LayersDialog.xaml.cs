using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using MouseMackros.Model;
using MouseMackros.Services;

namespace MouseMackros;

public partial class LayersDialog : Window
{
    private static readonly string[] Palette =
    {
        "#4C9AFF", "#3FB950", "#F2A33C", "#F0645A", "#9B6BF2", "#39B7C4"
    };

    private readonly AppConfig _config;
    private readonly Profile _parent;
    private readonly ObservableCollection<LayerRow> _rows = new();

    public LayersDialog(AppConfig config, Profile parent)
    {
        InitializeComponent();
        _config = config;
        _parent = parent;

        ParentText.Text = parent.Name;
        ParentColorBox.Text = parent.IndicatorColor ?? "";
        NextButtonBox.ItemsSource = ButtonChoices();
        PrevButtonBox.ItemsSource = ButtonChoices();
        NextButtonBox.SelectedIndex = Math.Clamp(parent.LayerNextButton, 0, 16);
        PrevButtonBox.SelectedIndex = Math.Clamp(parent.LayerPrevButton, 0, 16);

        ParentColorBox.TextChanged += (_, _) =>
            ParentColorPreview.Background = BrushOf(ParentColorBox.Text);
        LayerColorBox.TextChanged += (_, _) =>
            LayerColorPreview.Background = BrushOf(LayerColorBox.Text);

        LayersList.ItemsSource = _rows;
        RefreshLayers();
    }

    public bool Saved { get; private set; }

    private static string[] ButtonChoices()
    {
        var list = new string[17];
        list[0] = "нет";
        for (int i = 1; i <= 16; i++) list[i] = i.ToString();
        list[12] = "12 — ЛКМ";
        list[13] = "13 — ПКМ";
        list[14] = "14 — СКМ";
        return list;
    }

    private LayerRow? SelectedLayer => LayersList.SelectedItem as LayerRow;

    private void RefreshLayers(string? selectId = null)
    {
        selectId ??= SelectedLayer?.Model.Id;
        _rows.Clear();
        var layers = _config.ManualLayersOf(_parent);
        for (int i = 0; i < layers.Count; i++)
        {
            var layer = layers[i];
            _rows.Add(new LayerRow
            {
                Model = layer,
                Name = layer.Name,
                Subtitle = "слой " + (i + 1),
                ColorText = layer.IndicatorColor ?? "",
                Color = BrushOf(layer.IndicatorColor)
            });
        }

        var row = _rows.FirstOrDefault(r => r.Model.Id == selectId) ?? _rows.FirstOrDefault();
        LayersList.SelectedItem = row;
        if (row is null)
        {
            LayerColorBox.Text = "";
            LayerColorPreview.Background = Brushes.Transparent;
        }
    }

    private void LayersList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SelectedLayer is LayerRow row)
        {
            LayerColorBox.Text = row.Model.IndicatorColor ?? "";
        }
    }

    private void AddLayer_Click(object sender, RoutedEventArgs e)
    {
        int index = _config.ManualLayersOf(_parent).Count;
        if (index >= 6)
        {
            AppDialog.ShowInfo("Максимум 6 ручных слоёв на профиль.", "Ручные раскладки");
            return;
        }

        var layer = _parent.Clone();
        layer.Name = "Слой " + (index + 1);
        layer.ParentId = _parent.Id;
        layer.IsManualLayer = true;
        layer.IsFallback = false;
        layer.LayerOrder = index + 1;
        layer.LayerNextButton = 0;
        layer.LayerPrevButton = 0;
        layer.IndicatorColor = Palette[index % Palette.Length];
        layer.Match = new MatchRules();

        _config.Profiles.Add(layer);
        RefreshLayers(layer.Id);
    }

    private void RemoveLayer_Click(object sender, RoutedEventArgs e)
    {
        var row = SelectedLayer;
        if (row is null) return;
        if (!AppDialog.Confirm($"Удалить слой «{row.Model.Name}»?", "Ручные раскладки")) return;

        _config.Profiles.Remove(row.Model);
        RefreshLayers();
    }

    private void SaveLayerColor_Click(object sender, RoutedEventArgs e)
    {
        var row = SelectedLayer;
        if (row is null) return;
        if (!TryColor(LayerColorBox.Text, out var color))
        {
            AppDialog.ShowError("Некорректный цвет. Нужен RRGGBB или #RRGGBB.", "Цвет слоя");
            return;
        }

        row.Model.IndicatorColor = color;
        RefreshLayers(row.Model.Id);
    }

    private void LoadLayerPsf_Click(object sender, RoutedEventArgs e)
    {
        var row = SelectedLayer;
        if (row is null) return;

        var dialog = new OpenFileDialog
        {
            Title = "Раскладка MBF1000PRO для слоя",
            Filter = "Раскладка MBF1000PRO (*.psf)|*.psf|Все файлы (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) != true) return;

        var psf = PsfReader.Read(dialog.FileName);
        if (psf is null)
        {
            AppDialog.ShowError("Не удалось прочитать .psf файл.", "Раскладка");
            return;
        }
        if (psf.IsNeutralLayout)
        {
            AppDialog.ShowInfo("Это служебная раскладка MouseMackros.psf, её нельзя использовать как слой.",
                "Не тот файл");
            return;
        }

        int count = psf.ApplyTo(row.Model);
        RefreshLayers(row.Model.Id);
        LastInputText("Из " + System.IO.Path.GetFileName(dialog.FileName) + " загружено действий: " + count);
    }

    private void LastInputText(string text)
    {
        if (Owner is MainWindow main) main.SetStatusText(text);
    }

    private void PreviewColor_Click(object sender, RoutedEventArgs e)
    {
        string? text = SelectedLayer?.Model.IndicatorColor;
        if (string.IsNullOrWhiteSpace(text)) text = ParentColorBox.Text;
        if (!TryColor(text, out var color))
        {
            AppDialog.ShowError("Некорректный цвет. Нужен RRGGBB или #RRGGBB.", "Цвет колеса");
            return;
        }

        var result = HidMouseWriter.SetWheelColor(color);
        if (result.Success) AppDialog.ShowInfo(result.Message, "Цвет колеса");
        else AppDialog.ShowError(result.Message, "Цвет колеса");
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        string? parentColor = null;
        if (!string.IsNullOrWhiteSpace(ParentColorBox.Text))
        {
            if (!TryColor(ParentColorBox.Text, out var parsed))
            {
                AppDialog.ShowError("Некорректный цвет профиля. Нужен RRGGBB или #RRGGBB.", "Цвет профиля");
                return;
            }
            parentColor = parsed;
        }

        if (SelectedLayer is LayerRow row)
        {
            if (string.IsNullOrWhiteSpace(LayerColorBox.Text))
            {
                row.Model.IndicatorColor = null;
            }
            else if (!TryColor(LayerColorBox.Text, out var layerColor))
            {
                AppDialog.ShowError("Некорректный цвет слоя. Нужен RRGGBB или #RRGGBB.", "Цвет слоя");
                return;
            }
            else
            {
                row.Model.IndicatorColor = layerColor;
            }
        }

        _parent.IndicatorColor = parentColor;
        _parent.LayerNextButton = Math.Clamp(NextButtonBox.SelectedIndex, 0, 16);
        _parent.LayerPrevButton = Math.Clamp(PrevButtonBox.SelectedIndex, 0, 16);

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

    private static Brush BrushOf(string? text)
    {
        try
        {
            if (TryColor(text, out var color))
                return new SolidColorBrush((System.Windows.Media.Color)
                    System.Windows.Media.ColorConverter.ConvertFromString(color));
        }
        catch
        {
            // ignore
        }
        return Brushes.Transparent;
    }

    public sealed class LayerRow
    {
        public Profile Model { get; init; } = null!;
        public string Name { get; init; } = "";
        public string Subtitle { get; init; } = "";
        public string ColorText { get; init; } = "";
        public Brush Color { get; init; } = Brushes.Transparent;
    }
}
