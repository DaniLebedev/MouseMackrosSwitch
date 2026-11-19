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
    private readonly ActionExecutor _executor = new();

    private readonly Dictionary<string, Dictionary<string, Binding>> _lookup = new(StringComparer.OrdinalIgnoreCase);
    private bool _lookupDirty = true;
    private string? _manualProfileId;

    public AppConfig Config { get; private set; }
    public Profile ActiveProfile { get; private set; }
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

        _keyboard = new KeyboardHook();
        _keyboard.KeyEvent += OnKey;
        _mouse = new MouseHook();
        _mouse.MouseEvent += OnMouse;
        _watcher = new WindowWatcher();
        _watcher.ForegroundChanged += OnForeground;

        _executor.ProfileSwitchRequested += id => Notice?.Invoke(this, "profile:" + id);
        _executor.Error += msg => Error?.Invoke(this, msg);
    }

    public void Start()
    {
        _watcher.Start();
        ProfileStore.Log($"[start] профиль «{ActiveProfile.Name}»");
    }
    public void ApplyConfig(AppConfig config)
    {
        Config = config;
        _lookupDirty = true;
        _manualProfileId = null;
        var window = _watcher.GetForeground();
        if (window is not null) CurrentWindow = window;
        ActiveProfile = Resolve(CurrentWindow);
        ActiveProfileChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Invalidate() => _lookupDirty = true;

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

    public void SwitchProfile(string id)
    {
        var profile = Config.GetProfile(id);
        if (profile is null) return;
        _manualProfileId = profile.Id;
        ActiveProfile = profile;
        ActiveProfileChanged?.Invoke(this, EventArgs.Empty);
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
            ActiveProfile = profile;
            ActiveProfileChanged?.Invoke(this, EventArgs.Empty);
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
        ActiveProfile = profile;
        ActiveProfileChanged?.Invoke(this, EventArgs.Empty);
    }

    private Profile Resolve(WindowInfo? window)
    {
        if (_manualProfileId is not null)
        {
            var manual = Config.GetProfile(_manualProfileId);
            if (manual is not null) return manual;
        }
        if (window is not null)
        {
            Profile? best = null;
            foreach (var p in Config.Profiles)
            {
                if (p.IsFallback || !p.Match.HasRules) continue;
                if (!Matches(p.Match, window)) continue;
                if (best is null || p.Match.Priority > best.Match.Priority) best = p;
            }
            if (best is not null) return best;
        }

        return Config.FallbackProfile;
    }

    private static bool Matches(MatchRules rules, WindowInfo window)
    {
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

        var binding = MapFor(ActiveProfile).GetValueOrDefault(key);
        if (binding is null) return;

        ProfileStore.Log($"[action] {ActiveProfile.Name} | {trigger.DisplayName} | {binding.Action.Kind} | {binding.Label}");
        InputObserved?.Invoke(this, new InputEventInfo(DateTime.Now, "Клавиатура", trigger.DisplayName, key));

        if (binding.Action.Kind == ActionKind.PassThrough) return;
        e.Handled = true;

        if (e.IsDown) _executor.ExecuteStart(ActiveProfile.Id + "|" + key, binding.Action);
        else _executor.ExecuteEnd(ActiveProfile.Id + "|" + key, binding.Action);
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
        InputObserved?.Invoke(this, new InputEventInfo(DateTime.Now, "Мышь", e.DisplayName, key));

        var binding = MapFor(ActiveProfile).GetValueOrDefault(key);
        if (binding is null) return;
        if (binding.Action.Kind == ActionKind.PassThrough) return;

        ProfileStore.Log($"[action] {ActiveProfile.Name} | {e.DisplayName} | {binding.Action.Kind} | {binding.Label}");
        e.Handled = true;
        if (e.IsDown) _executor.ExecuteStart(ActiveProfile.Id + "|" + key, binding.Action);
        else _executor.ExecuteEnd(ActiveProfile.Id + "|" + key, binding.Action);
    }

    public void Dispose()
    {
        _keyboard.Dispose();
        _mouse.Dispose();
        _watcher.Dispose();
        _executor.Dispose();
    }
}
