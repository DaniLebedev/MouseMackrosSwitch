namespace MouseMackros.Model;

using System.Text.Json.Serialization;

/// <summary>What a bound button should do.</summary>
public enum ActionKind
{
    /// <summary>Do nothing special, let the original input through.</summary>
    PassThrough,
    /// <summary>Swallow the input and do nothing.</summary>
    Block,
    /// <summary>Send a key combination, e.g. "Alt+Left".</summary>
    Keys,
    /// <summary>Type arbitrary text.</summary>
    Text,
    /// <summary>Run a multi-line macro script.</summary>
    Macro,
    /// <summary>Send a media key (play, next, volume...).</summary>
    Media,
    /// <summary>Launch a program or open a file / URL.</summary>
    Run,
    /// <summary>Switch to another profile and keep it until the window changes.</summary>
    Profile,
    /// <summary>Scroll the wheel.</summary>
    Scroll
}

public enum MacroMode
{
    Once,
    Hold,
    Toggle
}

public sealed class ActionSpec
{
    public ActionKind Kind { get; set; } = ActionKind.Keys;

    /// <summary>Depends on <see cref="Kind"/>: combination, text, media name, path or profile id.</summary>
    public string Value { get; set; } = "";

    /// <summary>Program arguments for <see cref="ActionKind.Run"/>.</summary>
    public string? Arguments { get; set; }

    /// <summary>Macro script, used when <see cref="Kind"/> is <see cref="ActionKind.Macro"/>.</summary>
    public string? Script { get; set; }

    public MacroMode Mode { get; set; } = MacroMode.Once;

    /// <summary>How many extra times to repeat a macro (0 = play once).</summary>
    public int Repeat { get; set; }

    public ActionSpec Clone() => new()
    {
        Kind = Kind,
        Value = Value,
        Arguments = Arguments,
        Script = Script,
        Mode = Mode,
        Repeat = Repeat
    };

    public static ActionSpec Pass() => new() { Kind = ActionKind.PassThrough };
}

public sealed class Binding
{
    /// <summary>
    /// MBF1000PRO button number (1..16) this binding belongs to, or 0 for a
    /// keyboard / wheel / extra trigger that has no physical button.
    /// </summary>
    public int Button { get; set; }

    /// <summary>Serialized trigger, see <see cref="Input.Trigger.ToString"/>.</summary>
    public string Trigger { get; set; } = "";

    /// <summary>Optional friendly label shown in the editor.</summary>
    public string? Label { get; set; }

    public ActionSpec Action { get; set; } = ActionSpec.Pass();

    public bool Enabled { get; set; } = true;

    public Input.Trigger GetTrigger() => Input.Trigger.Parse(Trigger);
}

/// <summary>Rules that decide when a profile becomes active.</summary>
public sealed class MatchRules
{
    /// <summary>Process names (without .exe), wildcards like "chrome*" allowed.</summary>
    public List<string> Processes { get; set; } = new();

    /// <summary>Substring of the window title (case insensitive).</summary>
    public string? TitleContains { get; set; }

    /// <summary>Window class name (exact, case insensitive).</summary>
    public string? WindowClass { get; set; }

    public int Priority { get; set; }

    [JsonIgnore]
    public bool HasRules =>
        Processes.Count > 0 || !string.IsNullOrWhiteSpace(TitleContains) || !string.IsNullOrWhiteSpace(WindowClass);
}

public sealed class Profile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("n");
    public string Name { get; set; } = "Новый профиль";
    public string Accent { get; set; } = "#4C9AFF";

    /// <summary>Full path to the .exe this profile is attached to, when picked in the UI.</summary>
    public string? ApplicationPath { get; set; }

    /// <summary>Optional .psf file with the mouse layout this profile expects.</summary>
    public string? VendorProfilePath { get; set; }

    public MatchRules Match { get; set; } = new();
    public List<Binding> Bindings { get; set; } = new();

    public bool IsFallback { get; set; }

    /// <summary>Short application name for the UI, e.g. "chrome" or "без приложения".</summary>
    [JsonIgnore]
    public string ApplicationName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(ApplicationPath))
                return Path.GetFileNameWithoutExtension(ApplicationPath);
            return Match.Processes.FirstOrDefault() ?? "без приложения";
        }
    }

    public Binding? Find(string triggerKey) =>
        Bindings.FirstOrDefault(b => b.Enabled &&
                                     string.Equals(b.Trigger, triggerKey, StringComparison.OrdinalIgnoreCase));

    public Binding? FindButton(int button) => Bindings.FirstOrDefault(b => b.Button == button);

    public Profile Clone() => new()
    {
        Id = Guid.NewGuid().ToString("n"),
        Name = Name,
        Accent = Accent,
        IsFallback = false,
        ApplicationPath = ApplicationPath,
        VendorProfilePath = VendorProfilePath,
        Match = new MatchRules
        {
            Processes = Match.Processes.ToList(),
            TitleContains = Match.TitleContains,
            WindowClass = Match.WindowClass,
            Priority = Match.Priority
        },
        Bindings = Bindings.Select(b => new Binding
        {
            Button = b.Button,
            Trigger = b.Trigger,
            Label = b.Label,
            Enabled = b.Enabled,
            Action = b.Action.Clone()
        }).ToList()
    };
}

public sealed class AppConfig
{
    public int Version { get; set; } = 1;
    public bool Enabled { get; set; } = true;
    public bool ShowToast { get; set; } = true;
    public bool StartWithWindows { get; set; }
    public string ToggleHotkey { get; set; } = "Ctrl+Alt+P";
    public string DefaultProfileId { get; set; } = "default";
    public List<Profile> Profiles { get; set; } = new();

    public Profile? GetProfile(string id) =>
        Profiles.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

    public Profile FallbackProfile =>
        Profiles.FirstOrDefault(p => p.Id == DefaultProfileId)
        ?? Profiles.FirstOrDefault(p => p.IsFallback)
        ?? Profiles.FirstOrDefault()
        ?? new Profile { Id = "default", Name = "Общий", IsFallback = true };
}
