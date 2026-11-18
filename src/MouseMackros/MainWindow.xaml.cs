using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using MouseMackros.Engine;
using MouseMackros.Input;
using MouseMackros.Interop;
using MouseMackros.Model;
using MouseMackros.Services;
using Color = System.Windows.Media.Color;

namespace MouseMackros;

public partial class MainWindow : Window
{
    private readonly App _app;
    private readonly DispatcherTimer _saveTimer;
    private readonly ObservableCollection<ProfileRow> _profileRows = new();

    private Profile? _selected;
    private bool _suppress;

    public MainWindow(App app)
    {
        InitializeComponent();
        _app = app;

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            _app.SaveConfig();
        };

        ProfileList.ItemsSource = _profileRows;

        _app.Engine.ActiveProfileChanged += OnEngineProfileChanged;
        _app.Engine.InputObserved += OnInputObserved;

        ReloadFromConfig();
        UpdateStatus();
        Loaded += (_, _) => UpdateStatus();
    }

    // ------------------------------------------------------------------ state

    public void ReloadFromConfig()
    {
        _suppress = true;
        try
        {
            var config = _app.Config;
            EnabledCheck.IsChecked = config.Enabled;
            ToastCheck.IsChecked = config.ShowToast;
            AutostartCheck.IsChecked = StartupService.IsEnabled();
            HotkeyBox.Text = config.ToggleHotkey;

            if (_selected is null || config.Profiles.All(p => p.Id != _selected.Id))
                _selected = config.FallbackProfile;

            RefreshProfileList();
        }
        finally
        {
            _suppress = false;
        }

        RefreshEditor();
        UpdateStatus();
    }

    private void RefreshProfileList()
    {
        _suppress = true;
        try
        {
            var selectedId = _selected?.Id;
            _profileRows.Clear();
            foreach (var profile in _app.Config.Profiles)
            {
                _profileRows.Add(new ProfileRow
                {
                    Model = profile,
                    Name = profile.Name,
                    Subtitle = DescribeRules(profile),
                    Accent = ToBrush(profile.Accent),
                    ActiveMark = ReferenceEquals(profile, _app.Engine.ActiveProfile) ? "●" : ""
                });
            }

            var row = _profileRows.FirstOrDefault(r => r.Model.Id == selectedId) ?? _profileRows.FirstOrDefault();
            ProfileList.SelectedItem = row;
            if (row is not null) _selected = row.Model;
        }
        finally
        {
            _suppress = false;
        }
    }

    private static string DescribeRules(Profile profile)
    {
        if (profile.IsFallback) return "когда ничего не подошло";
        var parts = new List<string>();
        if (profile.Match.Processes.Count > 0)
            parts.Add(string.Join(", ", profile.Match.Processes.Take(3)) +
                      (profile.Match.Processes.Count > 3 ? "…" : ""));
        if (!string.IsNullOrWhiteSpace(profile.Match.TitleContains))
            parts.Add("\"" + profile.Match.TitleContains + "\"");
        if (!string.IsNullOrWhiteSpace(profile.Match.WindowClass))
            parts.Add("класс " + profile.Match.WindowClass);
        return parts.Count == 0 ? "правила не заданы" : string.Join(" · ", parts);
    }

    private void RefreshEditor()
    {
        if (_selected is null) return;
        _suppress = true;
        try
        {
            ProfileNameBox.Text = _selected.Name;
            ProcessesBox.Text = string.Join(", ", _selected.Match.Processes);
            TitleBox.Text = _selected.Match.TitleContains ?? "";
            ClassBox.Text = _selected.Match.WindowClass ?? "";
            PriorityBox.Text = _selected.Match.Priority.ToString();

            var isDefault = _selected.Id == _app.Config.DefaultProfileId || _selected.IsFallback;
            MakeDefaultButton.IsEnabled = !isDefault;
            MakeDefaultButton.Content = isDefault ? "Основной профиль" : "Сделать основным";
            DeleteProfileButton.IsEnabled = !isDefault;
            DeleteProfileButton.Opacity = isDefault ? 0.45 : 1;

            ProfileHint.Text = _selected.IsFallback
                ? "Резервный профиль: работает в любых окнах, где нет совпадений с другими профилями."
                : "Профиль включается, когда активное окно совпадает с правилами ниже.";

            var rows = _selected.Bindings.Select(b => new BindingRow
            {
                Model = b,
                TriggerDisplay = b.GetTrigger().DisplayName,
                Label = string.IsNullOrWhiteSpace(b.Label) ? "" : b.Label,
                ActionSummary = DescribeAction(b.Action),
                ActionDetail = DescribeActionDetail(b.Action)
            }).ToList();

            BindingsGrid.ItemsSource = rows;
            BindingCount.Text = rows.Count == 0 ? "пока пусто" : $"{rows.Count} шт.";
        }
        finally
        {
            _suppress = false;
        }
    }

    private void Commit(bool refreshList = true)
    {
        _app.Engine.Invalidate();
        RefreshEditor();
        if (refreshList) RefreshProfileList();
        ScheduleSave();
    }

    private void ScheduleSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    // ------------------------------------------------------------------ header

    private void PauseButton_Click(object sender, RoutedEventArgs e)
    {
        _app.Engine.TogglePause();
        UpdateStatus();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Hide();

    private void UpdateStatus()
    {
        var paused = _app.Engine.IsPaused || !_app.Config.Enabled;
        StatusText.Text = _app.Engine.IsPaused
            ? "Пауза"
            : _app.Config.Enabled ? "Активно" : "Выключено";
        StatusDot.Fill = paused
            ? new SolidColorBrush(Color.FromRgb(0x8D, 0x96, 0xA8))
            : new SolidColorBrush(Color.FromRgb(0x3F, 0xB9, 0x50));

        var window = _app.Engine.CurrentWindow;
        ActiveWindowText.Text = window is null
            ? $"Активный профиль: {_app.Engine.ActiveProfile.Name}"
            : $"Активный профиль: {_app.Engine.ActiveProfile.Name}  ·  {window.ProcessName}";
    }

    public void OnEngineProfileChanged(object? sender, EventArgs e)
    {
        RefreshProfileList();
        UpdateStatus();
    }

    private void OnInputObserved(object? sender, InputEventInfo info)
    {
        LastInputText.Text = $"{info.Time:HH:mm:ss}  ·  {info.Description}  ({info.Source})";
    }

    // ------------------------------------------------------------------ profile list

    private void ProfileList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppress) return;
        if (ProfileList.SelectedItem is ProfileRow row)
        {
            _selected = row.Model;
            RefreshEditor();
        }
    }

    private void AddProfileButton_Click(object sender, RoutedEventArgs e)
    {
        var profile = new Profile
        {
            Name = "Новый профиль",
            Accent = "#4C9AFF",
            Match = new MatchRules { Priority = 10 }
        };
        _app.Config.Profiles.Add(profile);
        _selected = profile;
        _app.Engine.Invalidate();
        RefreshProfileList();
        RefreshEditor();
        ScheduleSave();
        ProfileNameBox.Focus();
        ProfileNameBox.SelectAll();
    }

    private void MakeDefaultButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        foreach (var p in _app.Config.Profiles) p.IsFallback = false;
        _selected.IsFallback = true;
        _app.Config.DefaultProfileId = _selected.Id;
        Commit();
    }

    private void DeleteProfileButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        if (!AppDialog.Confirm($"Удалить профиль «{_selected.Name}»?", "Удаление профиля")) return;
        _app.Config.Profiles.Remove(_selected);
        _selected = _app.Config.FallbackProfile;
        _app.Engine.Invalidate();
        RefreshProfileList();
        RefreshEditor();
        ScheduleSave();
    }

    // ------------------------------------------------------------------ rules

    private void Rules_Changed(object sender, TextChangedEventArgs e)
    {
        if (_suppress || _selected is null) return;

        _selected.Match.Processes = ProcessesBox.Text
            .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        _selected.Match.TitleContains = string.IsNullOrWhiteSpace(TitleBox.Text) ? null : TitleBox.Text.Trim();
        _selected.Match.WindowClass = string.IsNullOrWhiteSpace(ClassBox.Text) ? null : ClassBox.Text.Trim();
        _selected.Match.Priority = int.TryParse(PriorityBox.Text, out var pr) ? pr : 0;

        _app.Engine.Invalidate();
        RefreshProfileList();
        ScheduleSave();
    }

    private void ProfileNameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppress || _selected is null) return;
        _selected.Name = ProfileNameBox.Text;
        RefreshProfileList();
        ScheduleSave();
    }

    private void AddActiveWindowButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        var window = _app.Engine.CurrentWindow;
        var name = window?.ProcessName;
        if (string.IsNullOrWhiteSpace(name))
        {
            AppDialog.ShowInfo("Не удалось определить активное окно. Переключитесь в нужную программу и попробуйте снова.");
            return;
        }
        var list = ProcessesBox.Text
            .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        if (!list.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            list.Add(name);
            ProcessesBox.Text = string.Join(", ", list);
        }
        else
        {
            AppDialog.ShowInfo($"«{name}» уже добавлен в этот профиль.");
        }
    }

    // ------------------------------------------------------------------ bindings

    private void AddBindingButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;

        var trigger = TriggerLearnDialog.Capture(this, _app.Engine, _selected);
        if (trigger is null) return;

        var key = trigger.ToString();
        var existing = _selected.Bindings.FirstOrDefault(b =>
            string.Equals(b.Trigger, key, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            if (!AppDialog.Confirm(
                    $"Для «{trigger.DisplayName}» в этом профиле уже есть действие. Заменить его?",
                    "Кнопка уже назначена"))
                return;
            _selected.Bindings.Remove(existing);
        }

        var result = BindingDialog.Edit(this, _app, key, "", new ActionSpec { Kind = ActionKind.Keys, Value = "" },
            $"Новое действие для «{trigger.DisplayName}»");
        if (result is null) return;

        _selected.Bindings.Add(new Binding
        {
            Trigger = result.Trigger,
            Label = result.Label,
            Action = result.Action
        });
        Commit();
    }

    private void EditBinding_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null || sender is not FrameworkElement { DataContext: BindingRow row }) return;

        var result = BindingDialog.Edit(this, _app, row.Model.Trigger, row.Model.Label ?? "",
            row.Model.Action.Clone(), "Изменить действие");
        if (result is null) return;

        row.Model.Trigger = result.Trigger;
        row.Model.Label = result.Label;
        row.Model.Action = result.Action;
        Commit();
    }

    private void TestBinding_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: BindingRow row }) return;
        _app.Engine.TestAction(row.Model.Action);
        LastInputText.Text = "Тест: " + DescribeAction(row.Model.Action);
    }

    private void DeleteBinding_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null || sender is not FrameworkElement { DataContext: BindingRow row }) return;
        _selected.Bindings.Remove(row.Model);
        Commit();
    }

    private void DiagnosticsButton_Click(object sender, RoutedEventArgs e)
    {
        var window = new DiagnosticsWindow(_app.Engine) { Owner = this };
        window.ShowDialog();
    }

    // ------------------------------------------------------------------ settings

    private void EnabledCheck_Click(object sender, RoutedEventArgs e)
    {
        if (_suppress) return;
        _app.Config.Enabled = EnabledCheck.IsChecked == true;
        _app.Engine.Invalidate();
        UpdateStatus();
        ScheduleSave();
    }

    private void ToastCheck_Click(object sender, RoutedEventArgs e)
    {
        if (_suppress) return;
        _app.Config.ShowToast = ToastCheck.IsChecked == true;
        ScheduleSave();
    }

    private void AutostartCheck_Click(object sender, RoutedEventArgs e)
    {
        if (_suppress) return;
        _app.Config.StartWithWindows = AutostartCheck.IsChecked == true;
        StartupService.Apply(_app.Config.StartWithWindows);
        ScheduleSave();
    }

    private void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin)
            return;
        if (key == Key.Escape)
        {
            HotkeyBox.Text = _app.Config.ToggleHotkey;
            Keyboard.ClearFocus();
            return;
        }

        var vk = KeyInterop.VirtualKeyFromKey(key);
        if (vk == 0) return;

        var mods = Keyboard.Modifiers;
        if (mods == ModifierKeys.None)
        {
            LastInputText.Text = "Горячая клавиша должна содержать модификатор (Ctrl, Alt, Shift или Win)";
            return;
        }

        var combo = new List<string>();
        if (mods.HasFlag(ModifierKeys.Control)) combo.Add("Ctrl");
        if (mods.HasFlag(ModifierKeys.Shift)) combo.Add("Shift");
        if (mods.HasFlag(ModifierKeys.Alt)) combo.Add("Alt");
        if (mods.HasFlag(ModifierKeys.Windows)) combo.Add("Win");
        var text = string.Join("+", combo) + "+" + KeyNames.GetName(vk);

        _app.Config.ToggleHotkey = text;
        HotkeyBox.Text = text;
        ScheduleSave();
        Keyboard.ClearFocus();
    }

    private void PsfButton_Click(object sender, RoutedEventArgs e)
    {
        var result = PsfGenerator.Generate();
        if (result is null)
        {
            AppDialog.ShowError(
                "Не удалось найти профили MBF1000PRO Mouse.\n\n" +
                "Папка с профилями обычно лежит здесь:\n" +
                @"%APPDATA%\GamingMouseData*\config",
                "Профиль не создан");
            return;
        }

        var text =
            "Файл создан:\n" + result.Path + "\n\n" +
            "Что делать дальше:\n" +
            "1. Откройте MBF1000PRO Mouse.\n" +
            "2. Нажмите «Config» (внизу слева) → импорт профиля.\n" +
            "3. Выберите файл MouseMackros.psf и примените его к мыши.\n\n" +
            "После этого каждая дополнительная кнопка будет отправлять\n" +
            "сочетание Ctrl+Alt+Shift+A…M, а MouseMackros превратит его\n" +
            "в действие для активного окна.\n\n" +
            "Открыть папку с файлом?";

        if (AppDialog.Confirm(text, "Профиль для мыши готов"))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{result.Path}\"")
            {
                UseShellExecute = true
            });
        }
    }

    private void DataFolderButton_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo("explorer.exe", ConfigService.RootDir) { UseShellExecute = true });
    }

    // ------------------------------------------------------------------ import / export

    private void ImportButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Импорт профилей MouseMackros",
            Filter = "Профили MouseMackros (*.json)|*.json|Все файлы (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            var imported = ConfigService.Import(dialog.FileName);
            bool replace = AppDialog.Confirm(
                $"В файле {imported.Profiles.Count} профил(ей).\n\n" +
                "Да — заменить текущие профили.\n" +
                "Нет — добавить их к существующим.",
                "Импорт профилей");

            if (replace)
            {
                _app.Config.Profiles = imported.Profiles;
                _app.Config.DefaultProfileId = imported.DefaultProfileId;
            }
            else
            {
                foreach (var profile in imported.Profiles)
                {
                    var copy = profile.Clone();
                    copy.Name = profile.Name;
                    copy.IsFallback = false;
                    _app.Config.Profiles.Add(copy);
                }
            }

            _selected = _app.Config.FallbackProfile;
            _app.ReloadConfig(_app.Config);
            LastInputText.Text = "Импортировано профилей: " + imported.Profiles.Count;
        }
        catch (Exception ex)
        {
            AppDialog.ShowError("Не удалось импортировать профили.\n\n" + ex.Message);
        }
    }

    private void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Экспорт профилей MouseMackros",
            Filter = "Профили MouseMackros (*.json)|*.json",
            FileName = "mousemackros-profiles.json"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            ConfigService.Export(_app.Config, dialog.FileName);
            LastInputText.Text = "Профили сохранены в " + dialog.FileName;
        }
        catch (Exception ex)
        {
            AppDialog.ShowError("Не удалось сохранить файл.\n\n" + ex.Message);
        }
    }

    // ------------------------------------------------------------------ closing

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_app.Exiting)
        {
            e.Cancel = true;
            Hide();
            _app.TrayHint();
            return;
        }
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _app.Engine.ActiveProfileChanged -= OnEngineProfileChanged;
        _app.Engine.InputObserved -= OnInputObserved;
        base.OnClosed(e);
    }

    // ------------------------------------------------------------------ helpers

    private static Brush ToBrush(string hex)
    {
        try
        {
            return new SolidColorBrush((Color)System.Windows.Media.ColorConverter.ConvertFromString(hex));
        }
        catch
        {
            return new SolidColorBrush(Color.FromRgb(0x4C, 0x9A, 0xFF));
        }
    }

    public static string DescribeAction(ActionSpec action) => action.Kind switch
    {
        ActionKind.PassThrough => "Пропускать как есть",
        ActionKind.Block => "Блокировать кнопку",
        ActionKind.Keys => "Клавиши: " + KeyNames.DescribeCombo(action.Value),
        ActionKind.Text => "Текст: " + Shorten(action.Value, 60),
        ActionKind.Macro => "Макрос (" + MacroModeName(action.Mode) + ")",
        ActionKind.Media => "Медиа: " + MediaName(action.Value),
        ActionKind.Run => "Запустить: " + Shorten(action.Value, 60),
        ActionKind.Profile => "Сменить профиль",
        ActionKind.Scroll => $"Прокрутка: {action.Value}",
        _ => action.Kind.ToString()
    };

    public static string DescribeActionDetail(ActionSpec action) => action.Kind switch
    {
        ActionKind.Macro => $"{MacroScript.Parse(action.Script).Count} шагов" +
                            (action.Repeat > 0 ? $", повтор ×{action.Repeat}" : ""),
        ActionKind.Run when !string.IsNullOrWhiteSpace(action.Arguments) => "аргументы: " + action.Arguments,
        ActionKind.Keys => "однократное нажатие",
        _ => ""
    };

    private static string MacroModeName(MacroMode mode) => mode switch
    {
        MacroMode.Hold => "пока зажата",
        MacroMode.Toggle => "вкл/выкл",
        _ => "один раз"
    };

    public static string MediaName(string value) => value switch
    {
        "PlayPause" => "Play / пауза",
        "Next" => "следующий трек",
        "Previous" or "Prev" => "предыдущий трек",
        "Stop" => "стоп",
        "VolumeUp" => "громче",
        "VolumeDown" => "тише",
        "VolumeMute" or "Mute" => "без звука",
        _ => value
    };

    private static string Shorten(string text, int max) =>
        text.Length <= max ? text : text[..max] + "…";

    public sealed class ProfileRow
    {
        public Profile Model { get; init; } = null!;
        public string Name { get; init; } = "";
        public string Subtitle { get; init; } = "";
        public Brush Accent { get; init; } = Brushes.Gray;
        public string ActiveMark { get; init; } = "";
    }

    public sealed class BindingRow
    {
        public Binding Model { get; init; } = null!;
        public string TriggerDisplay { get; init; } = "";
        public string Label { get; init; } = "";
        public string ActionSummary { get; init; } = "";
        public string ActionDetail { get; init; } = "";
    }
}
