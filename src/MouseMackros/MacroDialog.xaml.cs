using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using MouseMackros.Engine;
using MouseMackros.Model;
using MouseMackros.Services;

namespace MouseMackros;

public partial class MacroDialog : Window
{
    private readonly ProfileEngine _engine;
    private readonly string _suggestedName;
    private string? _currentPath;

    public MacroDialog(ActionSpec? current, ProfileEngine engine, string? suggestedName = null)
    {
        InitializeComponent();
        _engine = engine;
        _suggestedName = SafeFileName(suggestedName ?? "macro");
        UpdateFileLabel();

        if (current is { Kind: ActionKind.Macro })
        {
            ScriptBox.Text = current.Script ?? "";
            if (!string.IsNullOrWhiteSpace(current.ScriptPath))
            {
                _currentPath = current.ScriptPath;
                if (File.Exists(_currentPath))
                {
                    try
                    {
                        ScriptBox.Text = File.ReadAllText(_currentPath);
                    }
                    catch
                    {
                        // keep the embedded script when the file is unreadable
                    }
                }
            }
            UpdateFileLabel();
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

    private ActionSpec BuildAction() => new()
    {
        Kind = ActionKind.Macro,
        Script = ScriptBox.Text,
        ScriptPath = _currentPath,
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

        if (!string.IsNullOrWhiteSpace(_currentPath))
        {
            try
            {
                File.WriteAllText(_currentPath, action.Script);
            }
            catch (Exception ex)
            {
                AppDialog.ShowError("Не удалось сохранить файл макроса:\n" + ex.Message, "Макрос");
                return;
            }
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

    private void LoadMacro_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Загрузить макрос",
            Filter = "Макросы (*.macro;*.txt)|*.macro;*.txt|Все файлы (*.*)|*.*",
            InitialDirectory = MacroFolder()
        };

        if (dialog.ShowDialog(this) != true) return;

        try
        {
            ScriptBox.Text = File.ReadAllText(dialog.FileName);
            _currentPath = dialog.FileName;
            UpdateFileLabel();
        }
        catch (Exception ex)
        {
            AppDialog.ShowError("Не удалось прочитать файл:\n" + ex.Message, "Макрос");
        }
    }

    private void SaveMacro_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Сохранить макрос",
            Filter = "Макросы (*.macro)|*.macro|Текст (*.txt)|*.txt|Все файлы (*.*)|*.*",
            InitialDirectory = MacroFolder(),
            FileName = _currentPath is null ? _suggestedName + ".macro" : Path.GetFileName(_currentPath),
            DefaultExt = ".macro",
            AddExtension = true
        };

        if (dialog.ShowDialog(this) != true) return;

        try
        {
            File.WriteAllText(dialog.FileName, ScriptBox.Text);
            _currentPath = dialog.FileName;
            UpdateFileLabel();
        }
        catch (Exception ex)
        {
            AppDialog.ShowError("Не удалось сохранить файл:\n" + ex.Message, "Макрос");
        }
    }

    private void OpenInVsCode_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_currentPath))
        {
            SaveMacro_Click(sender, e);
            if (string.IsNullOrWhiteSpace(_currentPath)) return;
        }
        else
        {
            try
            {
                File.WriteAllText(_currentPath, ScriptBox.Text);
            }
            catch (Exception ex)
            {
                AppDialog.ShowError("Не удалось сохранить файл:\n" + ex.Message, "Макрос");
                return;
            }
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "code",
                Arguments = $"--reuse-window --goto \"{_currentPath}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            AppDialog.ShowError(
                "Не удалось открыть VS Code. Проверьте, что команда `code` доступна в PATH.\n\n" +
                ex.Message, "VS Code");
        }
    }

    private void Help_Click(object sender, RoutedEventArgs e) =>
        HelpWindow.ShowHelp(this, "Подсказки по макросам", MacroScript.ToHelpText());

    private void UpdateFileLabel()
    {
        FileLabel.Text = _currentPath is null ? "" : Path.GetFileName(_currentPath);
        FileLabel.ToolTip = _currentPath;
    }

    private static string MacroFolder()
    {
        var dir = Path.Combine(ProfileStore.RootDir, "macros");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string SafeFileName(string name)
    {
        foreach (var ch in Path.GetInvalidFileNameChars())
            name = name.Replace(ch, '_');
        name = name.Trim();
        return name.Length == 0 ? "macro" : name;
    }
}
