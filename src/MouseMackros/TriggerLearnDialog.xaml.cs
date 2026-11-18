using System.Windows;
using MouseMackros.Engine;
using MouseMackros.Input;
using MouseMackros.Model;

namespace MouseMackros;

public partial class TriggerLearnDialog : Window
{
    private readonly ProfileEngine _engine;
    private bool _finished;

    private TriggerLearnDialog(ProfileEngine engine)
    {
        InitializeComponent();
        _engine = engine;
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    public Trigger? Result { get; private set; }

    public static Trigger? Capture(Window owner, ProfileEngine engine, Profile profile)
    {
        var dialog = new TriggerLearnDialog(engine) { Owner = owner };
        return dialog.ShowDialog() == true ? dialog.Result : null;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _engine.LearnMode = true;
        _engine.InputObserved += OnInput;
        _engine.TriggerCaptured += OnCaptured;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _engine.TriggerCaptured -= OnCaptured;
        _engine.InputObserved -= OnInput;
        _engine.LearnMode = false;
    }

    private void OnInput(object? sender, InputEventInfo info)
    {
        ComboText.Text = info.Description;
        SourceText.Text = info.Source;
    }

    private void OnCaptured(object? sender, Trigger trigger)
    {
        if (_finished) return;
        _finished = true;
        Result = trigger;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _finished = true;
        DialogResult = false;
        Close();
    }
}
