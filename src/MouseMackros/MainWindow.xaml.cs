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

    private Profile? _selected;
    private bool _suppress;

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

        _app.Engine.ActiveProfileChanged += OnEngineProfileChanged;
        _app.Engine.InputObserved += OnInputObserved;

        PreviewKeyDown += MainWindow_PreviewKeyDown;

        ReloadFromConfig();
        UpdateStatus();
        RefreshVendorStatus();
        Activated += (_, _) => RefreshVendorStatus();
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
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void AddRows(string? parentId, int depth)
            {
                foreach (var profile in _app.Config.ChildrenOf(parentId))
                {
                    if (!seen.Add(profile.Id)) continue;
                    AddRow(profile, depth);
                    AddRows(profile.Id, depth + 1);
                }
            }

            AddRows(null, 0);

            // Safety net for a broken parent link: show the profile at top level.
            foreach (var profile in _app.Config.Profiles)
            {
                if (seen.Contains(profile.Id)) continue;
                AddRow(profile, 0);
                AddRows(profile.Id, 1);
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

    private void AddRow(Profile profile, int depth)
    {
        _profileRows.Add(new ProfileRow
        {
            Model = profile,
            Name = string.IsNullOrWhiteSpace(profile.Name) ? "Без имени" : profile.Name,
            Subtitle = profile.IsManualLayer
                ? "слой · " + DescribeRules(profile)
                : DescribeRules(profile),
            Accent = AccentBrush(profile.Accent),
            Initial = InitialOf(profile.Name),
            Icon = AppCatalog.IconFor(profile.AllApplications.FirstOrDefault()),
            IndicatorBrush = IndicatorBrushOf(profile.IndicatorColor),
            IndicatorVisibility = string.IsNullOrWhiteSpace(profile.IndicatorColor)
                ? Visibility.Collapsed
                : Visibility.Visible,
            IndentWidth = depth * 16,
            ActiveVisibility = ReferenceEquals(profile, _app.Engine.ActiveProfile)
                ? Visibility.Visible
                : Visibility.Collapsed
        });
    }

    private static string DescribeRules(Profile profile)
    {
        if (profile.IsFallback) return "работает во всех окнах";

        var apps = profile.AllApplications;
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(profile.Match.TitleContains))
            parts.Add("\"" + profile.Match.TitleContains + "\"");
        if (!string.IsNullOrWhiteSpace(profile.Match.WindowClass))
            parts.Add("класс " + profile.Match.WindowClass);
        if (!string.IsNullOrWhiteSpace(profile.Match.Expression))
            parts.Add(Shorten(profile.Match.Expression!, 48));

        if (apps.Count > 1)
            parts.Add($"{apps.Count} прил.: " +
                      string.Join(", ", apps.Take(2).Select(Path.GetFileNameWithoutExtension)) +
                      (apps.Count > 2 ? "…" : ""));
        else if (apps.Count == 1)
            parts.Add(Path.GetFileName(apps[0]));
        else if (profile.Match.Processes.Count > 0)
            parts.Add(string.Join(", ", profile.Match.Processes.Take(2)) +
                      (profile.Match.Processes.Count > 2 ? "…" : ""));

        return parts.Count == 0 ? "приложение не выбрано" : string.Join(" · ", parts);
    }

    private void RefreshContent()
    {
        if (_selected is null) return;

        _suppress = true;
        try
        {
            ProfileNameBox.Text = _selected.Name;
            HeaderInitial.Text = InitialOf(_selected.Name);
            HeaderIcon.Source = AppCatalog.IconFor(_selected.AllApplications.FirstOrDefault());

            RefreshApps();

            RulesChipText.Text = DescribeRulesChip(_selected);
            RulesChipText.ToolTip = DescribeRulesChip(_selected);

            var psf = _selected.VendorProfilePath;
            PsfChipText.Text = string.IsNullOrWhiteSpace(psf)
                ? "раскладка .psf не загружена"
                : "psf: " + Path.GetFileName(psf);
            PsfChipText.ToolTip = psf ?? "Файл раскладки MBF1000PRO";
            PsfSourceText.Text = string.IsNullOrWhiteSpace(psf)
                ? ""
                : "источник: " + Path.GetFileName(psf);

            FallbackChip.Visibility = _selected.IsFallback ? Visibility.Visible : Visibility.Collapsed;
            RulesButton.IsEnabled = !_selected.IsFallback;

            var parent = _app.Config.GetProfile(_selected.ParentId);
            SubProfileChip.Visibility = parent is null ? Visibility.Collapsed : Visibility.Visible;
            SubProfileChipText.Text = parent is null
                ? ""
                : (_selected.IsManualLayer ? "слой: " : "подпрофиль: ") + parent.Name;
            SubProfileChip.ToolTip = parent is null
                ? null
                : (_selected.IsManualLayer
                    ? "Переключается вручную кнопками родительского профиля."
                    : "Включается, когда совпадают правила профиля «" + parent.Name + "» и его собственные.");

            WheelColorSwatch.Background = IndicatorBrushOf(_selected.IndicatorColor);
            WheelColorText.Text = string.IsNullOrWhiteSpace(_selected.IndicatorColor)
                ? "цвет колеса"
                : _selected.IndicatorColor!.ToUpperInvariant();

            var typingDelay = _selected.TypingDelayMs ?? _app.Config.TypingDelayMs;
            TypingSpeedText.Text = typingDelay < 0
                ? "набор: мгновенно"
                : $"набор: {typingDelay} мс";
            TypingSpeedText.ToolTip = _selected.TypingDelayMs is null
                ? "Как в настройках"
                : "Переопределено для профиля";

            var layerCount = _app.Config.ManualLayersOf(_selected).Count;
            LayersChip.Visibility = layerCount > 0 ? Visibility.Visible : Visibility.Collapsed;
            LayersChipText.Text = layerCount > 0 ? $"слоёв: {layerCount}" : "";
            LayersChip.ToolTip = layerCount > 0
                ? "Профиль → Ручные раскладки… (кнопки переключения и цвета колеса)"
                : null;
        }
        finally
        {
            _suppress = false;
        }

        BuildRows();
    }

    private void RefreshVendorStatus()
    {
        // The neutral layout is written over HID, so the vendor profile files are
        // no longer the source of truth and their warning would be misleading.
        VendorBanner.Visibility = Visibility.Collapsed;
    }

    private void RefreshApps()
    {
        if (_selected is null) return;

        var rows = _selected.IsFallback
            ? new List<AppChipRow>()
            : _selected.AllApplications
                .Select(app => new AppChipRow
                {
                    Path = app,
                    Name = Path.GetFileNameWithoutExtension(app)
                })
                .ToList();

        AppsList.ItemsSource = rows;
        AppsList.Visibility = rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        NoAppsText.Visibility = rows.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        NoAppsText.Text = _selected.IsFallback
            ? "Профиль работает во всех окнах"
            : "Приложение не выбрано — профиль не включится";
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
        if (!string.IsNullOrWhiteSpace(profile.Match.Expression))
            parts.Add(Shorten(profile.Match.Expression!, 64));

        return parts.Count == 0 ? "правила не заданы" : string.Join(" · ", parts);
    }

    private void BuildRows()
    {
        _buttonRows.Clear();
        if (_selected is null) return;

        var accent = AccentBrush(_selected.Accent);
        var tint = TintBrush(_selected.Accent);
        int assigned = 0;

        foreach (var info in MouseButtonMap.Editable)
        {
            var binding = _selected.Bindings.FirstOrDefault(b => b.Button == info.Number && b.Enabled);
            if (binding is null) continue;

            assigned++;
            _buttonRows.Add(new ButtonRow
            {
                Model = binding,
                Button = info.Number,
                NumberText = info.Number.ToString(),
                Title = string.IsNullOrWhiteSpace(binding.Label) ? info.Name : binding.Label!,
                TriggerText = $"Кнопка {info.Number} · {Trigger.Parse(binding.Trigger).DisplayName}",
                ActionSummary = DescribeAction(binding.Action),
                ActionDetail = DescribeActionDetail(binding.Action),
                Accent = accent,
                AccentBackground = tint,
                AssignedVisibility = Visibility.Visible
            });
        }

        BindingCountText.Text = assigned == 0 ? "нет действий" : $"{assigned} из {MouseButtonMap.Editable.Count()}";
        EmptyState.Visibility = assigned == 0 ? Visibility.Visible : Visibility.Collapsed;

        if (assigned == 0)
        {
            var hasPsf = !string.IsNullOrWhiteSpace(_selected.VendorProfilePath);
            EmptyTitle.Text = hasPsf ? "В .psf нет действий для кнопок" : "Раскладка .psf не загружена";
            EmptyHint.Text = hasPsf
                ? "Все кнопки в этом файле выключены (Disable). Выберите другой .psf."
                : "Загрузите .psf из MBF1000PRO — действия кнопок появятся здесь автоматически.";
        }
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
        MenuItemAt(menu, 2).IsEnabled = _selected is not null;
        MenuItemAt(menu, 3).IsEnabled = _selected is not null;
        MenuItemAt(menu, 5).IsEnabled = _selected is not null;
        MenuItemAt(menu, 7).IsEnabled = editable && _selected?.ParentId is null;
        MenuItemAt(menu, 8).IsEnabled = editable;
        OpenMenu(menu, (FrameworkElement)sender);
    }

    private void EditMenuButton_Click(object sender, RoutedEventArgs e) =>
        OpenMenu(Menu("EditMenu"), (FrameworkElement)sender);

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

        var profileName = _app.Engine.ActiveLayer is { } layer
            ? $"{_app.Engine.ActiveProfile.Name} / {layer.Name}"
            : _app.Engine.ActiveProfile.Name;
        var window = _app.Engine.CurrentWindow;
        ActiveWindowText.Text = window is null
            ? $"Активный профиль: {profileName}"
            : $"Активный профиль: {profileName}  ·  {window.ProcessName}";
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
        AddApp_Click(sender, e);
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

    private void AddSubProfile_Click(object sender, RoutedEventArgs e) => CreateSubProfile(false);

    private void AddSubProfileForWindow_Click(object sender, RoutedEventArgs e) => CreateSubProfile(true);

    /// <summary>
    /// Creates a nested profile. It copies the parent's apps and button layout,
    /// then adds one extra condition — usually a stable part of the window title
    /// ("YouTube", a document name, a game name...).
    /// </summary>
    private void CreateSubProfile(bool useCurrentWindow)
    {
        if (_selected is null) return;

        var parent = _selected;
        var window = useCurrentWindow ? _app.Engine.CurrentWindow : null;
        var suggested = window is null
            ? ""
            : SuggestTitle(window.Title, IsBrowser(window.ProcessName));
        var host = window?.Url is { } url ? UrlHost(url) : null;
        var label = !string.IsNullOrWhiteSpace(suggested)
            ? suggested
            : host ?? "";
        var condition = SuggestCondition(window?.Url, suggested);

        var dialog = new SubProfileDialog(
            parent.Name,
            string.IsNullOrWhiteSpace(label) ? parent.Name + " — подпрофиль" : parent.Name + " · " + label,
            condition,
            window?.Title ?? "",
            window?.Url ?? "") { Owner = this };

        if (dialog.ShowDialog() != true) return;

        var child = parent.Clone();
        child.Name = string.IsNullOrWhiteSpace(dialog.ProfileName)
            ? parent.Name + " — подпрофиль"
            : dialog.ProfileName.Trim();
        child.ParentId = parent.Id;
        child.IsFallback = false;
        child.IsManualLayer = false;
        child.LayerNextButton = 0;
        child.LayerPrevButton = 0;
        child.Match.TitleContains = null;
        child.Match.WindowClass = null;
        child.Match.Expression = dialog.Condition;
        child.Match.Priority = parent.Match.Priority;

        _app.Config.Profiles.Add(child);
        _selected = child;
        _app.Engine.Invalidate();
        RefreshProfileList();
        RefreshContent();
        ScheduleSave();

        LastInputText.Text = "Создан подпрофиль: " + child.Name;
        ProfileNameBox.Focus();
        ProfileNameBox.SelectAll();
    }

    private static bool IsBrowser(string process) =>
        process is "firefox" or "chrome" or "msedge" or "brave" or "opera" or "vivaldi"
            or "yandex" or "librewolf" or "thorium" or "arc";

    private static string? UrlHost(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.Host))
            return uri.Host;
        return null;
    }

    private static string SuggestCondition(string? url, string? titleSuggestion)
    {
        var host = UrlHost(url);
        if (!string.IsNullOrWhiteSpace(host))
            return $"browser.url contains \"{host}\"";

        if (!string.IsNullOrWhiteSpace(titleSuggestion))
            return $"title contains \"{titleSuggestion.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";

        return "";
    }

    /// <summary>Turns a browser title into a likely site name ("Video - YouTube" → "YouTube").</summary>
    private static string SuggestTitle(string title, bool browser)
    {
        var text = (title ?? "").Trim();
        if (text.Length == 0) return "";

        if (!browser) return text;

        foreach (var suffix in new[]
                 {
                     " - Mozilla Firefox", " — Mozilla Firefox",
                     " - Google Chrome", " - Microsoft Edge",
                     " - Opera", " - Vivaldi", " - Brave", " - Yandex"
                 })
        {
            if (text.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                text = text[..^suffix.Length].Trim();
                break;
            }
        }

        int dash = text.LastIndexOf(" - ", StringComparison.Ordinal);
        if (dash > 0 && dash + 3 < text.Length)
            text = text[(dash + 3)..].Trim();

        return text;
    }

    private void ProfileNameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppress || _selected is null) return;
        _selected.Name = ProfileNameBox.Text;
        HeaderInitial.Text = InitialOf(_selected.Name);
        RefreshProfileList();
        ScheduleSave();
    }

    private void AddApp_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;

        if (_selected.IsFallback)
        {
            if (!AppDialog.Confirm(
                    "Основной профиль работает во всех окнах, поэтому привязать его к приложению нельзя.\n\n" +
                    "Создать отдельный профиль для приложения?",
                    "Основной профиль"))
                return;

            CreateProfile();
        }

        var entry = AppPickerDialog.Pick(this);
        if (entry is null) return;

        var path = string.IsNullOrWhiteSpace(entry.Path) ? entry.ProcessName : entry.Path;
        if (string.IsNullOrWhiteSpace(path)) return;

        if (_selected.AllApplications.Any(a => string.Equals(a, path, StringComparison.OrdinalIgnoreCase)))
        {
            LastInputText.Text = "Это приложение уже добавлено в профиль";
            return;
        }

        _selected.Applications.Add(path);
        _selected.ApplicationPath ??= path;

        var process = string.IsNullOrWhiteSpace(entry.ProcessName)
            ? Path.GetFileNameWithoutExtension(path)
            : entry.ProcessName;
        if (!string.IsNullOrWhiteSpace(process) &&
            !_selected.Match.Processes.Contains(process, StringComparer.OrdinalIgnoreCase))
        {
            _selected.Match.Processes.Add(process);
        }

        if (string.IsNullOrWhiteSpace(_selected.Name) || _selected.Name == "Новый профиль")
            _selected.Name = entry.Name;

        Commit();
        Keyboard.ClearFocus();
        LastInputText.Text = "Добавлено приложение: " + entry.Name;
    }

    private void RemoveApp_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null || sender is not FrameworkElement { DataContext: AppChipRow row }) return;

        _selected.Applications.RemoveAll(a => string.Equals(a, row.Path, StringComparison.OrdinalIgnoreCase));
        _selected.ApplicationPath = _selected.Applications.FirstOrDefault();

        var process = Path.GetFileNameWithoutExtension(row.Path);
        if (!string.IsNullOrWhiteSpace(process) &&
            !_selected.AllApplications.Any(a =>
                string.Equals(Path.GetFileNameWithoutExtension(a), process, StringComparison.OrdinalIgnoreCase)))
        {
            _selected.Match.Processes.RemoveAll(p => string.Equals(p, process, StringComparison.OrdinalIgnoreCase));
        }

        Commit();
        LastInputText.Text = "Приложение убрано из профиля";
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

    private void Layers_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;

        var parent = _selected.IsManualLayer
            ? _app.Config.GetProfile(_selected.ParentId) ?? _selected
            : _selected;

        var dialog = new LayersDialog(_app.Config, parent) { Owner = this };
        if (dialog.ShowDialog() != true || !dialog.Saved) return;

        Commit();
        LastInputText.Text = "Ручные раскладки обновлены";
    }

    public void SetStatusText(string text) => LastInputText.Text = text;

    private void WheelColor_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;

        var dialog = new ColorPickerDialog(_selected.IndicatorColor) { Owner = this };
        if (dialog.ShowDialog() != true || !dialog.Saved) return;

        _selected.IndicatorColor = dialog.SelectedColor;
        Commit();
        LastInputText.Text = "Цвет колеса обновлён";
    }

    private void TypingSpeed_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;

        var current = _selected.TypingDelayMs?.ToString() ?? "";
        var result = AnnotationDialog.Edit(this, "Скорость набора",
            "Задержка между символами в мс. -1 — мгновенно, пусто — как в настройках.",
            current,
            "Скорость влияет на text, browser find/search/goto и ввод URL.");
        if (result is null) return;

        if (result.Length == 0)
        {
            _selected.TypingDelayMs = null;
        }
        else if (int.TryParse(result, out var delay) && delay >= -1 && delay <= 5000)
        {
            _selected.TypingDelayMs = delay;
        }
        else
        {
            AppDialog.ShowError("Укажите число от -1 до 5000 или оставьте поле пустым.",
                "Скорость набора");
            return;
        }

        Commit();
        LastInputText.Text = "Скорость набора обновлена";
    }

    private void MakeDefault_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;

        if (_selected.ParentId is not null)
        {
            AppDialog.ShowInfo(
                "Подпрофиль включается только внутри родительского профиля, поэтому\n" +
                "сделать его основным нельзя. Сначала создайте отдельный профиль.",
                "Основной профиль");
            return;
        }

        foreach (var profile in _app.Config.Profiles) profile.IsFallback = false;
        _selected.IsFallback = true;
        _app.Config.DefaultProfileId = _selected.Id;
        Commit();
    }

    private void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null || _selected.IsFallback) return;

        var doomed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { _selected.Id };
        bool grew;
        do
        {
            grew = false;
            foreach (var profile in _app.Config.Profiles)
            {
                if (AppConfig.ParentOf(profile) is string parentId &&
                    doomed.Contains(parentId) &&
                    doomed.Add(profile.Id))
                {
                    grew = true;
                }
            }
        } while (grew);

        int children = doomed.Count - 1;
        var question = children > 0
            ? $"Удалить профиль «{_selected.Name}» вместе с подпрофилями ({children})?"
            : $"Удалить профиль «{_selected.Name}»?";
        if (!AppDialog.Confirm(question, "Удаление профиля")) return;

        _app.Config.Profiles.RemoveAll(p => doomed.Contains(p.Id));
        _selected = _app.Config.FallbackProfile;
        _app.Engine.Invalidate();
        RefreshProfileList();
        RefreshContent();
        ScheduleSave();
    }

    // ------------------------------------------------------------------ bindings

    private ButtonRow? RowOf(object sender) =>
        sender is FrameworkElement { DataContext: ButtonRow row } ? row : null;

    private void Macro_Click(object sender, RoutedEventArgs e)
    {
        var row = RowOf(sender);
        if (row?.Model is null) return;

        var dialog = new MacroDialog(row.Model.Action, _app.Engine,
            $"{_selected?.Name ?? "macro"}-button{row.Model.Button}") { Owner = this };
        if (dialog.ShowDialog() != true) return;

        row.Model.Action = dialog.Action;
        if (string.IsNullOrWhiteSpace(row.Model.Label))
            row.Model.Label = "Макрос";
        Commit();
        LastInputText.Text = "Макрос сохранён";
    }

    private void Label_Click(object sender, RoutedEventArgs e)
    {
        var row = RowOf(sender);
        if (row?.Model is null) return;

        var text = AnnotationDialog.Edit(this, "Подпись кнопки",
            "Понятная пометка, чтобы помнить, что делает кнопка.", row.Model.Label ?? "");
        if (text is null) return;

        row.Model.Label = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        Commit();
        LastInputText.Text = "Подпись сохранена";
    }

    private async void TestBinding_Click(object sender, RoutedEventArgs e)
    {
        var row = RowOf(sender);
        if (row?.Model is null) return;

        LastInputText.Text = "Тест через 3 секунды — переключитесь в нужное окно";
        await Task.Delay(3000);

        _app.Engine.TestAction(row.Model.Action);
        LastInputText.Text = "Тест: " + DescribeAction(row.Model.Action);
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
            "3. Выберите MouseMackros.psf и нажмите «Применить»/«OK».\n" +
            "4. В списке профилей фирменной программы выберите\n" +
            "   MouseMackros — он должен стать активным.\n\n" +
            "ВАЖНО: на мыши должна быть активна именно эта\n" +
            "раскладка. Тогда каждая кнопка отправляет своё\n" +
            "Ctrl+Alt+Shift+A…M, а MouseMackros подменяет\n" +
            "действие в зависимости от активного окна.\n\n" +
            "Открыть папку с файлом?";

        if (VendorSetup.Detect() is { Known: true, UsesNeutralLayout: false } active)
            text = text.Replace("Открыть папку с файлом?",
                $"Сейчас на мыши активна раскладка «{active.ProfileName}».\n" +
                "После импорта переключитесь на MouseMackros.\n\nОткрыть папку с файлом?");

        if (AppDialog.Confirm(text, "Раскладка для мыши готова"))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{result.Path}\"")
            {
                UseShellExecute = true
            });
        }

        RefreshVendorStatus();
    }

    private void HidDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        var text = HidMouseWriter.ListDevices();
        ProfileStore.Log("[hid] " + text.Replace("\n", " | "));
        AppDialog.ShowInfo(text, "HID-диагностика");
    }

    private void WritePsfToMouse_Click(object sender, RoutedEventArgs e)
    {        var dialog = new OpenFileDialog
        {
            Title = "Записать раскладку в мышь",
            Filter = "Раскладка MBF1000PRO (*.psf)|*.psf|Все файлы (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) != true) return;

        if (!AppDialog.Confirm(
                "Записать кнопки из этого .psf в активный профиль мыши?\n\n" +
                "Сейчас переносятся только клавиши; макросы и подсветка — в разработке.",
                "Запись .psf в мышь"))
            return;

        var result = HidMouseWriter.WritePsf(dialog.FileName);
        if (!result.Success)
        {
            AppDialog.ShowError(result.Message, "Не удалось записать раскладку");
            return;
        }

        LastInputText.Text = "Раскладка .psf записана в мышь";
        AppDialog.ShowInfo(result.Message, "Готово");
    }

    private void WriteMouseAll_Click(object sender, RoutedEventArgs e)
    {
        if (!AppDialog.Confirm(
                "Записать нейтральную раскладку Ctrl+Alt+Shift+A…M во ВСЕ профили мыши?\n\n" +
                "Это заменит кнопочные раскладки во всех слотах. Профили MouseMackros не изменятся.",
                "Настройка мыши"))
            return;

        var result = HidMouseWriter.WriteNeutralAllProfiles();
        if (!result.Success)
        {
            AppDialog.ShowError(result.Message, "Не удалось записать раскладку");
            return;
        }

        LastInputText.Text = "Нейтральная раскладка записана во все профили мыши";
        AppDialog.ShowInfo(result.Message, "Готово");
    }

    private void WriteMouse_Click(object sender, RoutedEventArgs e)
    {
        if (!AppDialog.Confirm(
                "Записать нейтральную раскладку Ctrl+Alt+Shift+A…M прямо в мышь?\n\n" +
                "Текущие назначения кнопок будут заменены. Профили MouseMackros не изменятся.",
                "Настройка мыши"))
            return;

        var result = HidMouseWriter.WriteNeutral();
        if (!result.Success)
        {
            AppDialog.ShowError(result.Message, "Не удалось записать раскладку");
            return;
        }

        LastInputText.Text = "Нейтральная раскладка записана в мышь";
        AppDialog.ShowInfo(result.Message, "Готово");
    }

    private void LoadPsf_Click(object sender, RoutedEventArgs e)
    {        if (_selected is null) return;

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

        if (psf.IsNeutralLayout)
        {
            AppDialog.ShowInfo(
                "Это служебная раскладка MouseMackros.psf.\n\n" +
                "Её нужно импортировать в MBF1000PRO и сделать активной на мыши,\n" +
                "а в профиль приложения загружайте его собственный .psf\n" +
                "(firefox.psf, word.psf, xz.psf и т. п.).",
                "Не тот файл");
            return;
        }

        int count = psf.ApplyTo(_selected);
        Commit();

        LastInputText.Text = count == 0
            ? "В .psf все кнопки отключены (Disable)"
            : $"Из {Path.GetFileName(dialog.FileName)} загружено действий: {count}";

        // A freshly imported layout is only useful when the profile has an app.
        if (count > 0 && _selected.AllApplications.Count == 0)
            AddApp_Click(sender, e);
    }

    private void DataFolder_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo("explorer.exe", ProfileStore.RootDir) { UseShellExecute = true });

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsWindow(_app.Config) { Owner = this };
        if (dialog.ShowDialog() != true || !dialog.Changed) return;

        _app.Engine.Invalidate();
        RefreshContent();
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
            "Действия кнопок читаются из .psf профилей,\n" +
            "а переключение идёт по активному окну.\n\n" +
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

    private static Brush IndicatorBrushOf(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return Brushes.Transparent;
        try
        {
            return new SolidColorBrush((Color)System.Windows.Media.ColorConverter.ConvertFromString(hex));
        }
        catch
        {
            return Brushes.Transparent;
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
        ActionKind.Macro => DescribeMacro(action.Script) +
                            (action.Repeat > 0 ? $" · повтор ×{action.Repeat}" : ""),
        ActionKind.Run when !string.IsNullOrWhiteSpace(action.Arguments) => "аргументы: " + action.Arguments,
        ActionKind.Keys => "однократное нажатие",
        _ => ""
    };

    private static string DescribeMacro(string? script)
    {
        var steps = MacroScript.Parse(script);
        if (steps.Count == 0) return "пусто";

        var parts = new List<string>();
        foreach (var step in steps.Take(12))
        {
            switch (step.Kind)
            {
                case "delay":
                    parts.Add(step.Number + " мс");
                    break;
                case "text":
                    parts.Add("текст");
                    break;
                case "wheel":
                    parts.Add("колесо " + step.Key);
                    break;
                case "press":
                    parts.Add(PressLabel(step));
                    break;
                case "down":
                    parts.Add(KeyOf(step) + "↓");
                    break;
                case "up":
                    parts.Add(KeyOf(step) + "↑");
                    break;
                case "pressmouse":
                    parts.Add("клик " + MouseWord(step.Key));
                    break;
                case "downmouse":
                    parts.Add(MouseWord(step.Key) + "↓");
                    break;
                case "upmouse":
                    parts.Add(MouseWord(step.Key) + "↑");
                    break;
                default:
                    parts.Add(step.Kind);
                    break;
            }
        }

        var text = string.Join(" ", parts);
        if (steps.Count > 12) text += " …";
        return text;
    }

    private static string PressLabel(MacroStep step)
    {
        var names = new List<string>();
        if (step.Modifiers1 != 0) names.Add(KeyNames.GetName(step.Modifiers1));
        if (step.Modifiers2 != 0) names.Add(KeyNames.GetName(step.Modifiers2));
        names.Add(KeyOf(step));
        return string.Join("+", names);
    }

    private static string KeyOf(MacroStep step)
    {
        if (step.Key is null) return "?";
        return int.TryParse(step.Key, out var vk) ? KeyNames.GetName(vk) : step.Key;
    }

    private static string MouseWord(string? button) => button switch
    {
        "left" => "ЛКМ",
        "right" => "ПКМ",
        "middle" => "СКМ",
        "x1" => "X1",
        "x2" => "X2",
        _ => button ?? "мышь"
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
        public Brush IndicatorBrush { get; init; } = Brushes.Transparent;
        public Visibility IndicatorVisibility { get; init; } = Visibility.Collapsed;
        public double IndentWidth { get; init; }
        public Visibility ActiveVisibility { get; init; } = Visibility.Collapsed;
    }

    public sealed class AppChipRow
    {
        public string Path { get; init; } = "";
        public string Name { get; init; } = "";
    }

    public sealed class ButtonRow
    {
        public Binding? Model { get; init; }
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
