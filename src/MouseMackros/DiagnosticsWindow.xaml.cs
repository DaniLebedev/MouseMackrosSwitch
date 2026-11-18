using System.Collections.ObjectModel;
using System.Windows;
using MouseMackros.Engine;

namespace MouseMackros;

public partial class DiagnosticsWindow : Window
{
    public sealed record Row(string Time, string Text);

    private readonly ProfileEngine _engine;
    private readonly ObservableCollection<Row> _rows = new();

    public DiagnosticsWindow(ProfileEngine engine)
    {
        InitializeComponent();
        _engine = engine;
        EventList.ItemsSource = _rows;
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _engine.LearnMode = true;
        _engine.InputObserved += OnInput;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _engine.InputObserved -= OnInput;
        _engine.LearnMode = false;
    }

    private void OnInput(object? sender, InputEventInfo info)
    {
        _rows.Insert(0, new Row(info.Time.ToString("HH:mm:ss"), $"{info.Description}   ({info.Source})"));
        while (_rows.Count > 60) _rows.RemoveAt(_rows.Count - 1);
        CounterText.Text = $"Событий: {_rows.Count}";
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        _rows.Clear();
        CounterText.Text = "Событий: 0";
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
