using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using MouseMackros.Model;

namespace MouseMackros.Services;

/// <summary>Everything that is not a profile: toggles and the default profile id.</summary>
public sealed class AppSettings
{
    public int Version { get; set; } = 2;
    public bool Enabled { get; set; } = true;
    public bool ShowToast { get; set; } = true;
    public bool StartWithWindows { get; set; }
    public string ToggleHotkey { get; set; } = "Ctrl+Alt+P";
    public string DefaultProfileId { get; set; } = "default";
}

/// <summary>
/// Profile storage.
///
///   %APPDATA%\MouseMackros\settings.json
///   %APPDATA%\MouseMackros\profiles\&lt;id&gt;.json
///   %APPDATA%\MouseMackros\psf\*.psf
///
/// The folder can be moved with <c>--data-dir &lt;path&gt;</c> (used by the e2e test
/// so it never touches the live configuration).
/// </summary>
public static class ProfileStore
{
    private static string? _rootOverride;

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string RootDir
    {
        get
        {
            var dir = _rootOverride ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MouseMackros");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string ProfilesDir => Path.Combine(RootDir, "profiles");
    public static string PsfDir => Path.Combine(RootDir, "psf");
    public static string SettingsPath => Path.Combine(RootDir, "settings.json");
    public static string LogPath => Path.Combine(RootDir, "log.txt");

    /// <summary>Custom --data-dir, or null when the default %APPDATA% folder is used.</summary>
    public static string? DataDirectoryOverride => _rootOverride;

    /// <summary>Legacy single-file config from version 0.1.</summary>
    public static string LegacyConfigPath => Path.Combine(RootDir, "config.json");

    /// <summary>Call once at startup, before <see cref="Load"/>.</summary>
    public static void UseDataDirectory(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) return;
        _rootOverride = Path.GetFullPath(Environment.ExpandEnvironmentVariables(directory.Trim('"')));
    }

    // ------------------------------------------------------------------ load

    public static AppConfig Load()
    {
        Directory.CreateDirectory(ProfilesDir);

        var config = new AppConfig();
        var settings = ReadSettings();
        if (settings is not null)
        {
            config.Enabled = settings.Enabled;
            config.ShowToast = settings.ShowToast;
            config.StartWithWindows = settings.StartWithWindows;
            config.ToggleHotkey = string.IsNullOrWhiteSpace(settings.ToggleHotkey) ? "Ctrl+Alt+P" : settings.ToggleHotkey;
            config.DefaultProfileId = string.IsNullOrWhiteSpace(settings.DefaultProfileId) ? "default" : settings.DefaultProfileId;
            config.Version = Math.Max(2, settings.Version);
        }

        foreach (var profile in ReadProfiles(ProfilesDir)) config.Profiles.Add(profile);

        if (config.Profiles.Count == 0)
        {
            var legacy = TryLoadLegacy();
            if (legacy is not null) config = legacy;
            else config.Profiles.AddRange(Presets.CreateDefault().Profiles);
        }

        Normalize(config);
        Save(config);
        return config;
    }

    private static AppSettings? ReadSettings()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return null;
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath), JsonOptions);
        }
        catch (Exception ex)
        {
            Log("Не удалось прочитать settings.json: " + ex.Message);
            return null;
        }
    }

    private static IEnumerable<Profile> ReadProfiles(string directory)
    {
        if (!Directory.Exists(directory)) yield break;

        foreach (var file in Directory.GetFiles(directory, "*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            Profile? profile = null;
            try
            {
                profile = JsonSerializer.Deserialize<Profile>(File.ReadAllText(file), JsonOptions);
            }
            catch (Exception ex)
            {
                Log($"Не удалось прочитать профиль {Path.GetFileName(file)}: {ex.Message}");
                try
                {
                    // Keep the broken file instead of letting Save() delete it.
                    File.Move(file, file + ".bad", true);
                }
                catch
                {
                    // ignore
                }
            }

            if (profile is { Bindings: not null }) yield return profile;
        }
    }

    private static AppConfig? TryLoadLegacy()
    {
        try
        {
            if (!File.Exists(LegacyConfigPath)) return null;
            var legacy = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(LegacyConfigPath), JsonOptions);
            if (legacy is not { Profiles.Count: > 0 }) return null;

            Log("Импортирован старый config.json в новое хранилище профилей.");
            try
            {
                var archive = LegacyConfigPath + ".migrated";
                if (File.Exists(archive)) File.Delete(archive);
                File.Move(LegacyConfigPath, archive);
            }
            catch
            {
                // The import still worked; a leftover file is harmless.
            }

            return legacy;
        }
        catch (Exception ex)
        {
            Log("Не удалось прочитать старый config.json: " + ex.Message);
            return null;
        }
    }

    /// <summary>Repairs ids, button numbers and the fallback profile after loading.</summary>
    public static void Normalize(AppConfig config)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var counter = 0;

        foreach (var profile in config.Profiles)
        {
            if (string.IsNullOrWhiteSpace(profile.Id) || !seen.Add(profile.Id))
            {
                do
                {
                    profile.Id = "p" + (++counter);
                } while (!seen.Add(profile.Id));
            }

            MouseButtonMap.NormalizeBindings(profile);

            if (profile.Match.Processes.Count == 0 &&
                !string.IsNullOrWhiteSpace(profile.ApplicationPath) &&
                !profile.IsFallback)
            {
                var process = Path.GetFileNameWithoutExtension(profile.ApplicationPath);
                if (!string.IsNullOrWhiteSpace(process)) profile.Match.Processes.Add(process);
            }
        }

        if (config.Profiles.Count == 0)
            config.Profiles.AddRange(Presets.CreateDefault().Profiles);

        var fallback = config.Profiles.FirstOrDefault(p => p.IsFallback);
        if (fallback is null)
        {
            fallback = config.Profiles.FirstOrDefault(p => p.Id == config.DefaultProfileId) ?? config.Profiles[0];
            fallback.IsFallback = true;
        }

        foreach (var profile in config.Profiles)
            if (!ReferenceEquals(profile, fallback)) profile.IsFallback = false;

        config.DefaultProfileId = fallback.Id;
    }

    // ------------------------------------------------------------------ save

    public static void Save(AppConfig config)
    {
        try
        {
            Directory.CreateDirectory(ProfilesDir);

            var settings = new AppSettings
            {
                Version = 2,
                Enabled = config.Enabled,
                ShowToast = config.ShowToast,
                StartWithWindows = config.StartWithWindows,
                ToggleHotkey = config.ToggleHotkey,
                DefaultProfileId = config.DefaultProfileId
            };
            WriteAtomic(SettingsPath, JsonSerializer.Serialize(settings, JsonOptions));

            var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var profile in config.Profiles)
            {
                var fileName = SafeFileName(profile.Id) + ".json";
                keep.Add(fileName);
                WriteAtomic(Path.Combine(ProfilesDir, fileName),
                    JsonSerializer.Serialize(profile, JsonOptions));
            }

            foreach (var file in Directory.GetFiles(ProfilesDir, "*.json"))
                if (!keep.Contains(Path.GetFileName(file)))
                    File.Delete(file);
        }
        catch (Exception ex)
        {
            Log("Не удалось сохранить профили: " + ex.Message);
        }
    }

    private static string SafeFileName(string id)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(id.Select(c => invalid.Contains(c) || c == '.' ? '_' : c).ToArray()).Trim('_');
        return string.IsNullOrWhiteSpace(cleaned) ? Guid.NewGuid().ToString("n") : cleaned;
    }

    private static void WriteAtomic(string path, string content)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, content);
        File.Copy(tmp, path, true);
        File.Delete(tmp);
    }

    // ------------------------------------------------------------------ import / export

    public static void ExportProfile(Profile profile, string path) =>
        File.WriteAllText(path, JsonSerializer.Serialize(profile, JsonOptions));

    public static Profile ImportProfile(string path)
    {
        var profile = JsonSerializer.Deserialize<Profile>(File.ReadAllText(path), JsonOptions)
                      ?? throw new InvalidOperationException("Файл профиля пуст или повреждён.");

        // Imported profiles must not overwrite the current default one.
        profile.Id = Guid.NewGuid().ToString("n");
        profile.IsFallback = false;
        if (profile.Bindings is null) profile.Bindings = new List<Binding>();
        MouseButtonMap.NormalizeBindings(profile);
        return profile;
    }

    public static void ExportConfig(AppConfig config, string path) =>
        File.WriteAllText(path, JsonSerializer.Serialize(config, JsonOptions));

    public static AppConfig ImportConfig(string path)
    {
        var config = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(path), JsonOptions)
                     ?? throw new InvalidOperationException("Файл профиля пуст или повреждён.");
        if (config.Profiles.Count == 0) throw new InvalidOperationException("В файле нет ни одного профиля.");
        return config;
    }

    public static string EnsurePsfDirectory()
    {
        Directory.CreateDirectory(PsfDir);
        return PsfDir;
    }

    public static void Log(string message)
    {
        try
        {
            File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
        }
        catch
        {
            // ignore
        }
    }
}
