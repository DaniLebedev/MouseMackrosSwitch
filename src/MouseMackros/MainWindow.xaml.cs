using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
    private static readonly string[] AccentPalette =
    {
        "#4C9AFF", "#9B6BF2", "#3FB950", "#F0645A", "#F2A33C", "#39B7C4", "#E06BB4"
    };

    private readonly App _app;
    private readonly DispatcherTimer _saveTimer;
    private readonly ObservableCollection<ProfileRow> _profileRows = new();
    private readonly ObservableCollection<ButtonRow> _buttonRows = new();
    private readonly ObservableCollection<ButtonRow> _otherRows = new();

    private Profile? _selected;
    private bool _suppress;
    private bool _showAllButtons;

    public MainWindow(App app)
    {
        InitializeComponent();
        _app = app;

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            _app.SaveConfig();
        };

        ProfileList.ItemsSource = _profileRows;
        ButtonsList.ItemsSource = _buttonRows;
        OtherList.ItemsSource = _otherRows;

        _app.Engine.ActiveProfileChanged += OnEngineProfileChanged;
        _app.Engine.InputObserved += OnInputObserved;

        PreviewKeyDown += MainWindow_PreviewKeyDown;

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
            if (_selected is null || _app.Config.Profiles.All(p => p.Id != _selected.Id))
                _selected = _app.Config.FallbackProfile;

            RefreshProfileList();
        }
        finally
        {
            _suppress = false;
        }

        RefreshContent();
        UpdateStatus();
    }

    private void RefreshProfileList()
    {
        var selectedId = _selected?.Id;

        _suppress = true;
        try
        {
            _profileRows.Clear();
            foreach (var profile in _app.Config.Profiles)
            {
                _profileRows.Add(new ProfileRow
                {
                    Model = profile,
                    Name = string.IsNullOrWhiteSpace(profile.Name) ? "Без имени" : profile.Name,
                    Subtitle = DescribeRules(profile),
                    Accent = AccentBrush(profile.Accent),
                    Initial = InitialOf(profile.Name),
                    Icon = AppCatalog.IconFor(profile.ApplicationPath),
                    ActiveVisibility = ReferenceEquals(profile, _app.Engine.ActiveProfile)
                        ? Visibility.Visible
                        : Visibility.Collapsed
                });
            }

            ProfileCountText.Text = _profileRows.Count.ToString();

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
        if (profile.IsFallback)
            return string.IsNullOrWhiteSpace(profile.ApplicationPath)
                ? "работает во всех окнах"
                : profile.ApplicationName;

        if (!string.IsNullOrWhiteSpace(profile.ApplicationPath))
            return Path.GetFileName(profile.ApplicationPath);

        var parts = new List<string>();
        if (profile.Match.Processes.Count > 0)
            parts.Add(string.Join(", ", profile.Match.Processes.Take(2)) +
                      (profile.Match.Processes.Count > 2 ? "…" : ""));
        if (!string.IsNullOrWhiteSpace(profile.Match.TitleContains))
            parts.Add("\"" + profile.Match.TitleContains + "\"");
        if (!string.IsNullOrWhiteSpace(profile.Match.WindowClass))
            parts.Add("класс " + profile.Match.WindowClass);

        return parts.Count == 0 ? "приложение не выбрано" : string.Join(" · ", parts);
    }

    private void RefreshContent()
    {
        if (_selected is null) return;

        _suppress = true;
        try
        {
            ProfileNameBox.Text = _selected.Name;
            AppNameText.Text = string.IsNullOrWhiteSpace(_selected.ApplicationPath)
                ? (_selected.IsFallback
                    ? "Все приложения"
                    : _selected.Match.Processes.Count > 0
                        ? "Правила: " + string.Join(", ", _selected.Match.Processes.Take(2)) +
                          (_selected.Match.Processes.Count > 2 ? "…" : "")
                        : "Приложение не выбрано")
                : _selected.Name;
            AppPathText.Text = _selected.ApplicationPath
                ?? (_selected.IsFallback ? "профиль по умолчанию" : DescribeRules(_selected));

            HeaderInitial.Text = InitialOf(_selected.Name);
            HeaderIcon.Source = AppCatalog.IconFor(_selected.ApplicationPath);

            RulesChipText.Text = DescribeRulesChip(_selected);
            RulesChipText.ToolTip = DescribeRulesChip(_selected);

            var psf = _selected.VendorProfilePath;
            PsfChipText.Text = string.IsNullOrWhiteSpace(psf)
                ? "раскладка .psf не выбрана"
                : "psf: " + Path.GetFileName(psf);
            PsfChipText.ToolTip = psf ?? "Файл раскладки MBF1000PRO";

            FallbackChip.Visibility = _selected.IsFallback ? Visibility.Visible : Visibility.Collapsed;
            RulesButton.IsEnabled = !_selected.IsFallback;
            ShowAllToggle.IsChecked = _showAllButtons;
        }
        finally
        {
            _suppress = false;
        }

        BuildRows();
    }

    private static string DescribeRulesChip(Profile profile)
    {
        if (profile.IsFallback) return "по умолчанию";

        var parts = new List<string>();
        if (profile.Match.Processes.Count > 0)
            parts.Add(string.Join(", ", profile.Match.Processes.Take(3)) +
                      (profile.Match.Processes.Count > 3 ? "…" : ""));
        if (!string.IsNullOrWhiteSpace(profile.Match.TitleContains))
            parts.Add("\"" + profile.Match.TitleContains + "\"");
        if (!string.IsNullOrWhiteSpace(profile.Match.WindowClass))
            parts.Add("класс: " + profile.Match.WindowClass);

        return parts.Count == 0 ? "правила не заданы" : string.Join(" · ", parts);
    }

    private void BuildRows()
    {
        _buttonRows.Clear();
        _otherRows.Clear();
        if (_selected is null) return;

        var accent = AccentBrush(_selected.Accent);
        var tint = TintBrush(_selected.Accent);
        int assigned = 0;

        foreach (var info in MouseButtonMap.Editable)
        {
            var binding = _selected.Bindings.FirstOrDefault(b => b.Button == info.Number && b.Enabled);
            if (binding is not null) assigned++;
            else if (!_showAllButtons) continue;

            _buttonRows.Add(new ButtonRow
            {
                Model = binding,
                IsButton = true,
                Button = info.Number,
                NumberText = info.Number.ToString(),
                Title = string.IsNullOrWhiteSpace(binding?.Label) ? info.Name : binding!.Label!,
                TriggerText = binding is null
                    ? $"Ctrl+Alt+Shift+{info.Letter} · не назначено"
                    : $"Кнопка {info.Number} · {Trigger.Parse(binding.Trigger).DisplayName}",
                ActionSummary = binding is null ? "Без действия" : DescribeAction(binding.Action),
                ActionDetail = binding is null
                    ? "Нажмите «Изменить», чтобы назначить действие"
                    : DescribeActionDetail(binding.Action),
                Accent = accent,
                AccentBackground = tint,
                AssignedVisibility = binding is null ? Visibility.Collapsed : Visibility.Visible
            });
        }

        foreach (var binding in _selected.Bindings.Where(b => b.Button <= 0))
        {
            var trigger = Trigger.Parse(binding.Trigger);
            _otherRows.Add(new ButtonRow
            {
                Model = binding,
                IsButton = false,
                NumberText = "—",
                Title = string.IsNullOrWhiteSpace(binding.Label) ? trigger.DisplayName : binding.Label!,
                TriggerText = trigger.DisplayName,
                ActionSummary = DescribeAction(binding.Action),
                ActionDetail = DescribeActionDetail(binding.Action),
                Accent = accent,
                AccentBackground = tint,
                AssignedVisibility = Visibility.Visible
            });
        }

        BindingCountText.Text = $"{assigned} из {MouseButtonMap.Editable.Count()}";
        EmptyState.Visibility = assigned == 0 ? Visibility.Visible : Visibility.Collapsed;
        OtherSection.Visibility = _otherRows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Commit(bool refreshList = true)
    {
        _app.Engine.Invalidate();
        RefreshContent();
        if (refreshList) RefreshProfileList();
        ScheduleSave();
    }

    private void ScheduleSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    // ------------------------------------------------------------------ menus

    private ContextMenu Menu(string key) => (ContextMenu)Resources[key];

    private static MenuItem MenuItemAt(ContextMenu menu, int index) => (MenuItem)menu.Items[index];

    private static void OpenMenu(ContextMenu menu, FrameworkElement target)
    {
        menu.PlacementTarget = target;
        menu.Placement = PlacementMode.Bottom;
        menu.HorizontalOffset = 0;
        menu.VerticalOffset = -2;
        menu.IsOpen = true;
    }

    private void FileMenuButton_Click(object sender, RoutedEventArgs e) =>
        OpenMenu(Menu("FileMenu"), (FrameworkElement)sender);

    private void ProfileMenuButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = Menu("ProfileMenu");
        bool editable = _selected is { IsFallback: false };
        MenuItemAt(menu, 0).IsEnabled = _selected is not null;
        MenuItemAt(menu, 1).IsEnabled = _selected is { IsFallback: false };
        MenuItemAt(menu, 3).IsEnabled = editable;
        MenuItemAt(menu, 4).IsEnabled = editable;
        OpenMenu(menu, (FrameworkElement)sender);
    }

    private void EditMenuButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = Menu("EditMenu");
        MenuItemAt(menu, 2).IsChecked = _showAllButtons;
        OpenMenu(menu, (FrameworkElement)sender);
    }

    private void HelpMenuButton_Click(object sender, RoutedEventArgs e) =>
        OpenMenu(Menu("HelpMenu"), (FrameworkElement)sender);

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.N)
        {
            e.Handled = true;
            NewProfile_Click(sender, e);
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.O)
        {
            e.Handled = true;
            ImportProfile_Click(sender, e);
        }
    }

    // ------------------------------------------------------------------ title bar

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

    // ------------------------------------------------------------------ profiles

    private void ProfileList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppress) return;
        if (ProfileList.SelectedItem is ProfileRow row)
        {
            _selected = row.Model;
            RefreshContent();
        }
    }

    private void NewProfile_Click(object sender, RoutedEventArgs e)
    {
        CreateProfile();
        ChooseApp_Click(sender, e);
    }

    private Profile CreateProfile()
    {
        var index = _app.Config.Profiles.Count;
        var profile = new Profile
        {
            Name = "Новый профиль",
            Accent = AccentPalette[index % AccentPalette.Length],
            Match = new MatchRules { Priority = 10 }
        };

        _app.Config.Profiles.Add(profile);
        _selected = profile;
        _app.Engine.Invalidate();
        RefreshProfileList();
        RefreshContent();
        ScheduleSave();

        ProfileNameBox.Focus();
        ProfileNameBox.SelectAll();
        return profile;
    }

    private void ProfileNameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppress || _selected is null) return;
        _selected.Name = ProfileNameBox.Text;
        HeaderInitial.Text = InitialOf(_selected.Name);
        RefreshProfileList();
        ScheduleSave();
    }

    private void ChooseApp_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;

        if (_selected.IsFallback)
        {
            if (!AppDialog.Confirm(
                    "Основной профиль работает во всех окнах, поэтому привязать его к одному приложению нельзя.\n\n" +
                    "Создать отдельный профиль для приложения?",
                    "Основной профиль"))
                return;

            CreateProfile();
        }

        var entry = AppPickerDialog.Pick(this, _selected!.ApplicationPath);
        if (entry is null) return;

        _selected.ApplicationPath = entry.Path;
        _selected.Match.Processes = new List<string> { entry.ProcessName };

        if (string.IsNullOrWhiteSpace(_selected.Name) || _selected.Name == "Новый профиль")
            _selected.Name = entry.Name;

        Commit();
        Keyboard.ClearFocus();
        LastInputText.Text = "Профиль привязан к " + entry.Name;
    }

    private void Rules_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;

        if (_selected.IsFallback)
        {
            AppDialog.ShowInfo(
                "Основной профиль срабатывает там, где нет более точного совпадения,\n" +
                "поэтому его правила не используются.\n\n" +
                "Создайте отдельный профиль и выберите для него приложение.",
                "Правила профиля");
            return;
        }

        var dialog = new RulesDialog(_selected) { Owner = this };
        if (dialog.ShowDialog() != true || !dialog.Saved) return;

        Commit();
    }

    private void MakeDefault_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;

        foreach (var profile in _app.Config.Profiles) profile.IsFallback = false;
        _selected.IsFallback = true;
        _app.Config.DefaultProfileId = _selected.Id;
        Commit();
    }

    private void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null || _selected.IsFallback) return;
        if (!AppDialog.Confirm($"Удалить профиль «{_selected.Name}»?", "Удаление профиля")) return;

        _app.Config.Profiles.Remove(_selected);
        _selected = _app.Config.FallbackProfile;
        _app.Engine.Invalidate();
        RefreshProfileList();
        RefreshContent();
        ScheduleSave();
    }

    // ------------------------------------------------------------------ bindings

    private ButtonRow? RowOf(object sender) =>
        sender is FrameworkElement { DataContext: ButtonRow row } ? row : null;

    private void EditButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        var row = RowOf(sender);
        if (row is null) return;

        var trigger = row.Model?.Trigger
                      ?? (row.IsButton ? MouseButtonMap.TriggerFor(row.Button) : "");
        if (string.IsNullOrWhiteSpace(trigger)) return;

        var action = row.Model?.Action.Clone() ?? new ActionSpec { Kind = ActionKind.Keys, Value = "" };
        var title = row.IsButton ? $"Кнопка {row.Button}" : "Действие";

        var result = BindingDialog.Edit(this, _app, trigger, row.Model?.Label ?? "", action,
            row.Model is null ? "Назначить действие" : "Изменить действие");
        if (result is null) return;

        UpsertBinding(row, result);
    }

    private void CaptureBinding_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;

        var trigger = TriggerLearnDialog.Capture(this, _app.Engine, _selected);
        if (trigger is null) return;

        var key = trigger.ToString();
        int button = MouseButtonMap.ButtonForTrigger(key);

        Binding? existing = button > 0 ? _selected.FindButton(button) : _selected.Find(key);
        if (existing is not null &&
            !AppDialog.Confirm(
                $"Для «{trigger.DisplayName}» в этом профиле уже есть действие. Заменить его?",
                "Кнопка уже назначена"))
            return;

        var action = existing?.Action.Clone() ?? new ActionSpec { Kind = ActionKind.Keys, Value = "" };
        var result = BindingDialog.Edit(this, _app, key, existing?.Label ?? "", action,
            button > 0 ? $"Кнопка {button}" : "Новое действие");
        if (result is null) return;

        var row = new ButtonRow { Model = existing, IsButton = button > 0, Button = button };
        UpsertBinding(row, result);
    }

    private void UpsertBinding(ButtonRow row, BindingEditResult result)
    {
        if (_selected is null) return;

        var binding = row.Model;
        if (binding is null)
        {
            binding = new Binding { Button = row.IsButton ? row.Button : 0 };
            _selected.Bindings.Add(binding);
        }

        binding.Trigger = row.IsButton ? MouseButtonMap.TriggerFor(row.Button) : result.Trigger;
        binding.Label = result.Label;
        binding.Action = result.Action;
        binding.Enabled = true;

        Commit();
        LastInputText.Text = "Сохранено: " + (result.Label.Length > 0 ? result.Label : DescribeAction(result.Action));
    }

    private void TestBinding_Click(object sender, RoutedEventArgs e)
    {
        var row = RowOf(sender);
        if (row?.Model is null) return;

        _app.Engine.TestAction(row.Model.Action);
        LastInputText.Text = "Тест: " + DescribeAction(row.Model.Action);
    }

    private void RemoveButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        var row = RowOf(sender);
        if (row?.Model is null) return;

        _selected.Bindings.Remove(row.Model);
        Commit();
    }

    private void ShowAllToggle_Click(object sender, RoutedEventArgs e)
    {
        _showAllButtons = ShowAllToggle.IsChecked == true;
        BuildRows();
    }

    private void ShowAll_Click(object sender, RoutedEventArgs e)
    {
        _showAllButtons = !_showAllButtons;
        ShowAllToggle.IsChecked = _showAllButtons;
        BuildRows();
    }

    private void Diagnostics_Click(object sender, RoutedEventArgs e)
    {
        var window = new DiagnosticsWindow(_app.Engine) { Owner = this };
        window.ShowDialog();
    }

    // ------------------------------------------------------------------ files

    private void ImportProfile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Открыть профиль MouseMackros",
            Filter = "Профиль MouseMackros (*.json)|*.json|Все файлы (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            var profile = ProfileStore.ImportProfile(dialog.FileName);
            if (_app.Config.Profiles.Any(p => p.Name == profile.Name))
                profile.Name += " (импорт)";

            _app.Config.Profiles.Add(profile);
            _selected = profile;
            Commit();
            LastInputText.Text = "Импортирован профиль: " + profile.Name;
        }
        catch (Exception ex)
        {
            AppDialog.ShowError("Не удалось открыть профиль.\n\n" + ex.Message, "Импорт профиля");
        }
    }

    private void ExportProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;

        var name = string.Concat(_selected.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        var dialog = new SaveFileDialog
        {
            Title = "Сохранить профиль MouseMackros",
            Filter = "Профиль MouseMackros (*.json)|*.json",
            FileName = (string.IsNullOrWhiteSpace(name) ? "profile" : name) + ".json"
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            ProfileStore.ExportProfile(_selected, dialog.FileName);
            LastInputText.Text = "Профиль сохранён: " + dialog.FileName;
        }
        catch (Exception ex)
        {
            AppDialog.ShowError("Не удалось сохранить файл.\n\n" + ex.Message, "Экспорт профиля");
        }
    }

    private void GeneratePsf_Click(object sender, RoutedEventArgs e)
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
            "2. Config → импорт профиля.\n" +
            "3. Выберите MouseMackros.psf и примените его к мыши.\n\n" +
            "После этого кнопки 1–11, 15 и 16 будут отправлять\n" +
            "Ctrl+Alt+Shift+A…M, а MouseMackros превратит их\n" +
            "в действие для активного окна.\n\n" +
            "Открыть папку с файлом?";

        if (AppDialog.Confirm(text, "Раскладка для мыши готова"))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{result.Path}\"")
            {
                UseShellExecute = true
            });
        }
    }

    private void LoadPsf_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;

        var dialog = new OpenFileDialog
        {
            Title = "Раскладка MBF1000PRO",
            Filter = "Раскладка MBF1000PRO (*.psf)|*.psf|Все файлы (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) != true) return;

        var psf = PsfReader.Read(dialog.FileName);
        if (psf is null)
        {
            AppDialog.ShowError("Не удалось прочитать .psf файл.", "Раскладка");
            return;
        }

        _selected.VendorProfilePath = dialog.FileName;

        int updated = 0;
        int mapped = 0;
        foreach (var button in MouseButtonMap.Editable)
        {
            var key = psf.ForButton(button.Number);
            if (key?.Trigger is null) continue;
            mapped++;

            var binding = _selected.FindButton(button.Number);
            if (binding is null) continue;
            if (!string.Equals(binding.Trigger, key.Trigger, StringComparison.OrdinalIgnoreCase))
            {
                binding.Trigger = key.Trigger;
                updated++;
            }
        }

        Commit();
        LastInputText.Text = mapped == 0
            ? "В .psf не найдено назначений для кнопок мыши"
            : $"Раскладка загружена: кнопок {mapped}, обновлено триггеров {updated}";
    }

    private void DataFolder_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo("explorer.exe", ProfileStore.RootDir) { UseShellExecute = true });

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsWindow(_app.Config) { Owner = this };
        if (dialog.ShowDialog() != true || !dialog.Changed) return;

        _app.Engine.Invalidate();
        UpdateStatus();
        ScheduleSave();
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => _app.ExitApp();

    private void About_Click(object sender, RoutedEventArgs e)
    {
        var version = typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "0.1.0";
        AppDialog.ShowInfo(
            "MouseMackros " + version + "\n\n" +
            "Контекстные раскладки для мыши MBF1000PRO.\n" +
            "Кнопки отправляют Ctrl+Alt+Shift+A…M, а программа\n" +
            "подменяет их действиями активного профиля.\n\n" +
            "Пауза: " + _app.Config.ToggleHotkey + "\n" +
            "Профили: " + ProfileStore.RootDir,
            "О программе");
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

    private static string InitialOf(string? name) =>
        string.IsNullOrWhiteSpace(name) ? "?" : name.Trim()[..1].ToUpperInvariant();

    private static Brush AccentBrush(string hex)
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

    private static Brush TintBrush(string hex)
    {
        try
        {
            var color = (Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
            return new SolidColorBrush(Color.FromArgb(0x2A, color.R, color.G, color.B));
        }
        catch
        {
            return new SolidColorBrush(Color.FromArgb(0x2A, 0x4C, 0x9A, 0xFF));
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
        public string Initial { get; init; } = "";
        public Brush Accent { get; init; } = Brushes.Gray;
        public ImageSource? Icon { get; init; }
        public Visibility ActiveVisibility { get; init; } = Visibility.Collapsed;
    }

    public sealed class ButtonRow
    {
        public Binding? Model { get; init; }
        public bool IsButton { get; init; }
        public int Button { get; init; }
        public string NumberText { get; init; } = "";
        public string Title { get; init; } = "";
        public string TriggerText { get; init; } = "";
        public string ActionSummary { get; init; } = "";
        public string ActionDetail { get; init; } = "";
        public Brush Accent { get; init; } = Brushes.Gray;
        public Brush AccentBackground { get; init; } = Brushes.Transparent;
        public Visibility AssignedVisibility { get; init; } = Visibility.Collapsed;
    }
}
