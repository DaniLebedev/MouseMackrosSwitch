using System.Text.RegularExpressions;
using MouseMackros.Input;
using MouseMackros.Interop;
using MouseMackros.Model;
using MouseMackros.Services;

namespace MouseMackros.Engine;

public sealed record InputEventInfo(DateTime Time, string Source, string Description, string TriggerKey);

/// <summary>Glue between global hooks, the active profile and the action executor.</summary>
public sealed class ProfileEngine : IDisposable
{
    private readonly KeyboardHook _keyboard;
    private readonly MouseHook _mouse;
    private readonly WindowWatcher _watcher;
    private readonly ActionExecutor _executor;

    private readonly Dictionary<string, Dictionary<string, Binding>> _lookup = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _layerIndex = new(StringComparer.OrdinalIgnoreCase);
    private readonly SynchronizationContext? _uiContext = SynchronizationContext.Current;
    private bool _lookupDirty = true;
    private string? _manualProfileId;
    private string? _lastWheelColor;

    public AppConfig Config { get; private set; }
    public Profile ActiveProfile { get; private set; }
    public Profile? ActiveLayer { get; private set; }
    public WindowInfo? CurrentWindow { get; private set; }
    public bool IsPaused { get; set; }

    /// <summary>While true, inputs are captured for the "learn button" feature instead of being executed.</summary>
    public bool LearnMode { get; set; }

    public event EventHandler<Trigger>? TriggerCaptured;
    public event EventHandler<InputEventInfo>? InputObserved;
    public event EventHandler? ActiveProfileChanged;
    public event EventHandler<string>? Notice;
    public event EventHandler<string>? Error;

    public ProfileEngine(AppConfig config)
    {
        Config = config;
        ActiveProfile = config.FallbackProfile;
        _executor = new ActionExecutor(
            () => CurrentWindow,
            () => ActiveLayer?.TypingDelayMs ??
                  ActiveProfile.TypingDelayMs ??
                  Config.TypingDelayMs);

        _keyboard = new KeyboardHook();
        _keyboard.KeyEvent += OnKey;
        _mouse = new MouseHook();
        _mouse.MouseEvent += OnMouse;
        _watcher = new WindowWatcher();
        _watcher.ForegroundChanged += OnForeground;

        _executor.ProfileSwitchRequested += id =>
            Post(() => Notice?.Invoke(this, "profile:" + id));
        _executor.ProfileCycleRequested += direction =>
            Post(() => CycleProfile(direction));
        _executor.LayerCycleRequested += direction =>
            Post(() => SwitchLayer(direction));
        _executor.LayerSetRequested += index =>
            Post(() => SetLayer(index));
        _executor.Error += msg => Post(() => Error?.Invoke(this, msg));
    }

    public void Start()
    {
        _watcher.Start();
        ActiveLayer = ResolveLayer(ActiveProfile);
        ProfileStore.Log($"[start] профиль «{ActiveProfile.Name}»{LayerSuffix}");
        UpdateWheelColor();
    }

    public void ApplyConfig(AppConfig config)
    {
        Config = config;
        _lookupDirty = true;
        _manualProfileId = null;
        if (CurrentWindow is null)
        {
            var window = _watcher.GetForeground();
            if (window is not null) CurrentWindow = window;
        }
        Activate(Resolve(CurrentWindow));
    }

    public void Invalidate()
    {
        _lookupDirty = true;
        UpdateWheelColor();
    }

    /// <summary>Runs an action once, for the "Test" button in the editor.</summary>
    public void TestAction(ActionSpec action)
    {
        var copy = action.Clone();
        if (copy.Kind == ActionKind.Macro && copy.Mode != MacroMode.Once)
        {
            copy.Mode = MacroMode.Once;
            copy.Repeat = Math.Max(1, copy.Repeat);
        }
        _executor.ExecuteStart("__test__", copy);
    }

    private void Post(Action action)
    {
        if (_uiContext is not null &&
            !ReferenceEquals(SynchronizationContext.Current, _uiContext))
        {
            _uiContext.Post(_ => action(), null);
            return;
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(action);
            return;
        }

        action();
    }

    public void SwitchProfile(string id)
    {
        var profile = Config.GetProfile(id);
        if (profile is null) return;

        // Selecting a manual layer means "the parent profile + this layer".
        if (profile.IsManualLayer && Config.GetProfile(profile.ParentId) is { } parent)
        {
            _manualProfileId = parent.Id;
            ActiveProfile = parent;
            var layers = GetManualLayers(parent);
            int index = layers.ToList().IndexOf(profile);
            if (index >= 0) _layerIndex[parent.Id] = index + 1;
            ActiveLayer = profile;
            ActiveProfileChanged?.Invoke(this, EventArgs.Empty);
            UpdateWheelColor();
            return;
        }

        _manualProfileId = profile.Id;
        Activate(profile);
    }

    public IReadOnlyList<Profile> GetManualLayers(Profile parent) => Config.ManualLayersOf(parent);

    private string LayerSuffix => ActiveLayer is null ? "" : " / «" + ActiveLayer.Name + "»";

    private void Activate(Profile profile)
    {
        ActiveProfile = profile;
        ActiveLayer = ResolveLayer(profile);
        ActiveProfileChanged?.Invoke(this, EventArgs.Empty);
        UpdateWheelColor();
    }

    private Profile? ResolveLayer(Profile parent)
    {
        var layers = GetManualLayers(parent);
        if (layers.Count == 0)
        {
            _layerIndex.Remove(parent.Id);
            return null;
        }

        // Index 0 is the parent profile itself; 1..N are manual layers.
        int index = _layerIndex.GetValueOrDefault(parent.Id, 0);
        if (index < 0 || index > layers.Count) index = 0;
        _layerIndex[parent.Id] = index;
        return index == 0 ? null : layers[index - 1];
    }

    /// <summary>Cycles the parent profile and its manual layers (+1/-1, wraps).</summary>
    public void SwitchLayer(int direction)
    {
        var layers = GetManualLayers(ActiveProfile);
        if (layers.Count == 0) return;

        int count = layers.Count + 1;
        int index = _layerIndex.GetValueOrDefault(ActiveProfile.Id, 0);
        index = ((index + direction) % count + count) % count;
        _layerIndex[ActiveProfile.Id] = index;
        ActiveLayer = index == 0 ? null : layers[index - 1];

        var name = ActiveLayer?.Name ?? "базовая";
        ProfileStore.Log($"[layer] «{ActiveProfile.Name}» → «{name}» ({index + 1}/{count})");
        ActiveProfileChanged?.Invoke(this, EventArgs.Empty);
        UpdateWheelColor();
    }

    /// <summary>Selects a manual layer by number: 0 is the base profile.</summary>
    public void SetLayer(int index)
    {
        var layers = GetManualLayers(ActiveProfile);
        if (layers.Count == 0)
        {
            Notice?.Invoke(this, "У профиля нет ручных слоёв");
            return;
        }

        index = Math.Clamp(index, 0, layers.Count);
        _layerIndex[ActiveProfile.Id] = index;
        ActiveLayer = index == 0 ? null : layers[index - 1];

        var name = ActiveLayer?.Name ?? "базовая";
        ProfileStore.Log($"[layer] «{ActiveProfile.Name}» → «{name}» ({index + 1}/{layers.Count + 1})");
        ActiveProfileChanged?.Invoke(this, EventArgs.Empty);
        UpdateWheelColor();
    }

    /// <summary>Switches to the next/previous ordinary profile.</summary>
    public void CycleProfile(int direction)
    {
        var profiles = Config.Profiles
            .Where(profile => !profile.IsFallback && !profile.IsManualLayer &&
                              string.IsNullOrWhiteSpace(profile.ParentId))
            .ToList();

        if (profiles.Count <= 1)
        {
            Notice?.Invoke(this, "Нет других профилей");
            return;
        }

        int index = profiles.FindIndex(profile => profile.Id == ActiveProfile.Id);
        if (index < 0) index = 0;
        index = ((index + direction) % profiles.Count + profiles.Count) % profiles.Count;

        var target = profiles[index];
        ProfileStore.Log($"[profile] ручное переключение → «{target.Name}»");
        SwitchProfile(target.Id);
    }

    private void UpdateWheelColor()
    {
        var color = ActiveLayer?.IndicatorColor;
        if (string.IsNullOrWhiteSpace(color)) color = ActiveProfile.IndicatorColor;
        if (string.IsNullOrWhiteSpace(color)) return;
        if (string.Equals(color, _lastWheelColor, StringComparison.OrdinalIgnoreCase)) return;

        _lastWheelColor = color;
        var label = ActiveProfile.Name + LayerSuffix;
        Task.Run(() =>
        {
            var result = HidMouseWriter.SetWheelColor(color);
            if (!result.Success) _lastWheelColor = null;
            ProfileStore.Log("[wheel] " + (result.Success
                ? result.Message + " (" + label + ")"
                : result.Message));
        });
    }

    public void TogglePause()
    {
        IsPaused = !IsPaused;
        _executor.StopAll();
        Notice?.Invoke(this, IsPaused ? "Пауза: макросы выключены" : "Макросы снова активны");
    }

    private bool Effective => Config.Enabled && !IsPaused;

    // ---------------------------------------------------------------- matching

    private void OnForeground(object? sender, WindowInfo info)
    {
        if (string.Equals(info.ProcessName, "mousemackros", StringComparison.OrdinalIgnoreCase))
        {
            // Our own windows must not reset the active profile.
            return;
        }

        CurrentWindow = info;
        _manualProfileId = null;
        var profile = Resolve(info);
        if (!ReferenceEquals(profile, ActiveProfile))
        {
            ProfileStore.Log($"[profile] «{profile.Name}» ← {info.ProcessName} / {info.Title}");
            Activate(profile);
        }
    }

    /// <summary>
    /// Resolves the profile from the window that is active right now. Foreground
    /// events can be missed, so every input also refreshes the active profile.
    /// </summary>
    private void SyncActiveProfile(bool pointer)
    {
        // The hook callback can run on a thread that has no desktop, where
        // GetForegroundWindow returns 0. The UI timer keeps CurrentWindow fresh.
        var window = CurrentWindow;
        if (window is null) window = _watcher.GetForeground();
        if (window is null) window = _watcher.GetPointed();

        if (window is null) return;
        if (string.Equals(window.ProcessName, "mousemackros", StringComparison.OrdinalIgnoreCase)) return;

        CurrentWindow = window;
        var profile = Resolve(window);
        if (ReferenceEquals(profile, ActiveProfile)) return;

        ProfileStore.Log($"[input-profile] «{profile.Name}» ← {window.ProcessName}");
        Activate(profile);
    }

    private Profile Resolve(WindowInfo? window) => SelectProfile(Config, window, _manualProfileId);

    /// <summary>
    /// Picks the most specific profile for a window. Nested profiles win over
    /// their parents, so a "YouTube" sub-profile of "Браузер" takes over only
    /// on YouTube pages and only while the browser itself also matches.
    /// </summary>
    public static Profile SelectProfile(AppConfig config, WindowInfo? window, string? manualId = null)
    {
        if (!string.IsNullOrWhiteSpace(manualId))
        {
            var manual = config.GetProfile(manualId);
            if (manual is { IsManualLayer: true } &&
                config.GetProfile(manual.ParentId) is { } parent)
            {
                return parent;
            }
            if (manual is not null) return manual;
        }

        if (window is not null)
        {
            Profile? best = null;
            int bestDepth = -1;

            foreach (var p in config.Profiles)
            {
                if (p.IsFallback || p.IsManualLayer || !p.Match.HasRules) continue;
                if (!Matches(p.Match, window)) continue;
                if (!AncestorsMatch(config, p, window)) continue;

                int depth = config.DepthOf(p);
                if (best is null || depth > bestDepth ||
                    (depth == bestDepth && p.Match.Priority > best.Match.Priority))
                {
                    best = p;
                    bestDepth = depth;
                }
            }

            if (best is not null) return best;
        }

        return config.FallbackProfile;
    }

    private static bool AncestorsMatch(AppConfig config, Profile profile, WindowInfo window)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { profile.Id };
        var current = profile;

        while (AppConfig.ParentOf(current) is string parentId && seen.Add(parentId))
        {
            var parent = config.GetProfile(parentId);
            if (parent is null) break;
            if (parent.Match.HasRules && !Matches(parent.Match, window)) return false;
            current = parent;
        }

        return true;
    }

    private static bool Matches(MatchRules rules, WindowInfo window)
    {
        if (!string.IsNullOrWhiteSpace(rules.Expression))
        {
            if (!ExpressionEvaluator.TryEvaluate(rules.Expression, window, out bool ok, out _) || !ok)
                return false;
        }

        if (rules.Processes.Count > 0)
        {
            var ok = rules.Processes.Any(pattern => Wildcard(pattern, window.ProcessName));
            if (!ok) return false;
        }
        if (!string.IsNullOrWhiteSpace(rules.TitleContains) &&
            !window.Title.Contains(rules.TitleContains, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        if (!string.IsNullOrWhiteSpace(rules.WindowClass) &&
            !string.Equals(rules.WindowClass, window.ClassName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        return true;
    }

    public static bool Wildcard(string pattern, string text)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return false;
        pattern = pattern.Trim();
        if (pattern.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            pattern = pattern[..^4];
        var regex = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
        return Regex.IsMatch(text, regex, RegexOptions.IgnoreCase);
    }

    // ---------------------------------------------------------------- bindings

    private Dictionary<string, Binding> MapFor(Profile profile)
    {
        if (_lookupDirty) RebuildLookup();
        return _lookup.TryGetValue(profile.Id, out var map) ? map : new Dictionary<string, Binding>();
    }

    /// <summary>Active manual layer first, then the parent profile.</summary>
    private Binding? FindBinding(string key)
    {
        if (ActiveLayer is not null &&
            MapFor(ActiveLayer).TryGetValue(key, out var layerBinding))
        {
            return layerBinding;
        }
        return MapFor(ActiveProfile).GetValueOrDefault(key);
    }

    private void RebuildLookup()
    {
        _lookup.Clear();
        foreach (var p in Config.Profiles)
        {
            var map = new Dictionary<string, Binding>(StringComparer.OrdinalIgnoreCase);
            foreach (var b in p.Bindings)
            {
                if (!b.Enabled) continue;
                var trig = Trigger.Parse(b.Trigger);
                if (!trig.IsEmpty) map[trig.ToString()] = b;
            }
            _lookup[p.Id] = map;
        }
        _lookupDirty = false;
    }

    // ---------------------------------------------------------------- input

    private void OnKey(object? sender, KeyInputEventArgs e)
    {
        SyncActiveProfile(false);
        var trigger = Trigger.Key(e.Combo);
        var key = trigger.ToString();

        if (e.IsDown && e.Modifiers.Count >= 3)
            ProfileStore.Log($"[key] {trigger.DisplayName} профиль={ActiveProfile.Name}");

        if (LearnMode)
        {
            InputObserved?.Invoke(this, new InputEventInfo(DateTime.Now, "Клавиатура", trigger.DisplayName, key));
            e.Handled = true;
            if (e.IsDown && !KeyboardHook.IsModifier(e.Vk))
            {
                TriggerCaptured?.Invoke(this, trigger);
            }
            return;
        }

        if (e.IsDown && !KeyboardHook.IsModifier(e.Vk) &&
            TryParseHotkey(Config.ToggleHotkey, out var hotMods, out var hotVk) &&
            e.Vk == hotVk && hotMods.SetEquals(e.Modifiers))
        {
            e.Handled = true;
            TogglePause();
            return;
        }

        if (!Effective) return;

        // Manual layer controls are profile-wide and always win over bindings.
        int button = MouseButtonMap.ButtonForTrigger(trigger.ToString());
        int layerDirection = 0;
        if (button > 0)
        {
            if (button == ActiveProfile.LayerNextButton) layerDirection = 1;
            else if (button == ActiveProfile.LayerPrevButton) layerDirection = -1;
        }
        if (layerDirection != 0)
        {
            if (e.IsDown) SwitchLayer(layerDirection);
            e.Handled = true;
            return;
        }

        var binding = FindBinding(key);
        if (binding is null)
        {
            // Ctrl+Alt+Shift+A..M is MouseMackros' own channel. If the current
            // profile has no action for a button, swallow the chord instead of
            // leaking it into the application.
            if (IsNeutralTrigger(trigger)) e.Handled = true;
            return;
        }

        ProfileStore.Log($"[action] {ActiveProfile.Name}{LayerSuffix} | {trigger.DisplayName} | {binding.Action.Kind} | {binding.Label}");
        InputObserved?.Invoke(this, new InputEventInfo(DateTime.Now, "Клавиатура", trigger.DisplayName, key));

        if (binding.Action.Kind == ActionKind.PassThrough) return;
        e.Handled = true;

        var executionKey = ActiveProfile.Id + "|" + (ActiveLayer?.Id ?? "") + "|" + key;
        if (e.IsDown) _executor.ExecuteStart(executionKey, binding.Action);
        else _executor.ExecuteEnd(executionKey, binding.Action);
    }

    private static bool IsNeutralTrigger(Trigger trigger)
    {
        var text = trigger.ToString();
        foreach (var info in MouseButtonMap.Editable)
        {
            if (string.Equals(MouseButtonMap.TriggerFor(info.Number), text,
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private bool TryParseHotkey(string combo, out HashSet<int> modifiers, out int vk)
    {
        modifiers = new HashSet<int>();
        vk = 0;
        if (!KeyNames.TryParseCombo(combo, out var mods, out vk)) return false;
        foreach (var m in mods) modifiers.Add(KeyNames.NormalizeModifier(m));
        return true;
    }

    private void OnMouse(object? sender, MouseInputEventArgs e)
    {
        SyncActiveProfile(true);
        var key = e.Trigger.ToString();
        if (LearnMode)
        {
            InputObserved?.Invoke(this, new InputEventInfo(DateTime.Now, "Мышь", e.DisplayName, key));
            e.Handled = true;
            if (e.IsDown) TriggerCaptured?.Invoke(this, e.Trigger);
            return;
        }

        if (!Effective) return;

        // Manual layers can be switched by LMB/RMB/MB too; those buttons are
        // delivered as mouse events, not as the neutral keyboard triggers.
        int button = MouseButtonMap.ButtonForTrigger(key);
        int layerDirection = 0;
        if (button > 0)
        {
            if (button == ActiveProfile.LayerNextButton) layerDirection = 1;
            else if (button == ActiveProfile.LayerPrevButton) layerDirection = -1;
        }
        if (layerDirection != 0)
        {
            InputObserved?.Invoke(this, new InputEventInfo(DateTime.Now, "Мышь", e.DisplayName, key));
            if (e.IsDown) SwitchLayer(layerDirection);
            e.Handled = true;
            return;
        }

        var binding = FindBinding(key);
        if (binding is null) return;
        if (binding.Action.Kind == ActionKind.PassThrough) return;

        InputObserved?.Invoke(this, new InputEventInfo(DateTime.Now, "Мышь", e.DisplayName, key));

        ProfileStore.Log($"[action] {ActiveProfile.Name}{LayerSuffix} | {e.DisplayName} | {binding.Action.Kind} | {binding.Label}");
        e.Handled = true;
        var executionKey = ActiveProfile.Id + "|" + (ActiveLayer?.Id ?? "") + "|" + key;
        if (e.IsDown) _executor.ExecuteStart(executionKey, binding.Action);
        else _executor.ExecuteEnd(executionKey, binding.Action);
    }

    public void Dispose()
    {
        _keyboard.Dispose();
        _mouse.Dispose();
        _watcher.Dispose();
        _executor.Dispose();
    }
}
