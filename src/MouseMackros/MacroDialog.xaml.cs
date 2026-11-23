using System.Windows;
using MouseMackros.Engine;
using MouseMackros.Model;

namespace MouseMackros;

public partial class MacroDialog : Window
{
    private readonly ProfileEngine _engine;

    public MacroDialog(ActionSpec? current, ProfileEngine engine)
    {
        InitializeComponent();
        _engine = engine;

        if (current is { Kind: ActionKind.Macro })
        {
            ScriptBox.Text = current.Script ?? "";
            ModeBox.SelectedIndex = current.Mode switch
            {
                MacroMode.Hold => 1,
                MacroMode.Toggle => 2,
                _ => 0
            };
            RepeatBox.Text = current.Repeat.ToString();
        }
        else
        {
            ScriptBox.Text = "press mouse:right\ndelay 5000";
            ModeBox.SelectedIndex = 0;
            RepeatBox.Text = "0";
        }

        Loaded += (_, _) =>
        {
            ScriptBox.Focus();
            ScriptBox.SelectAll();
        };
    }

    public ActionSpec Action { get; private set; } =
        new() { Kind = ActionKind.Macro, Script = "" };

    public string HelpText => MacroScript.ToHelpText();

    private ActionSpec BuildAction() => new()
    {
        Kind = ActionKind.Macro,
        Script = ScriptBox.Text,
        Mode = ModeBox.SelectedIndex switch
        {
            1 => MacroMode.Hold,
            2 => MacroMode.Toggle,
            _ => MacroMode.Once
        },
        Repeat = int.TryParse(RepeatBox.Text.Trim(), out var repeat)
            ? Math.Clamp(repeat, 0, 1000)
            : 0
    };

    private void Test_Click(object sender, RoutedEventArgs e)
    {
        var action = BuildAction();
        if (string.IsNullOrWhiteSpace(action.Script))
        {
            AppDialog.ShowError("Скрипт макроса пуст.", "Макрос");
            return;
        }

        _engine.TestAction(action);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var action = BuildAction();
        if (string.IsNullOrWhiteSpace(action.Script))
        {
            AppDialog.ShowError("Скрипт макроса пуст.", "Макрос");
            return;
        }

        Action = action;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
