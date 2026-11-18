using System.Text.Json;
using System.Text.Json.Serialization;

namespace MouseMackros.Services;

public static class ConfigService
{
    public static string RootDir
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MouseMackros");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string ConfigPath => Path.Combine(RootDir, "config.json");
    public static string LogPath => Path.Combine(RootDir, "log.txt");

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static Model.AppConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                var cfg = JsonSerializer.Deserialize<Model.AppConfig>(json, JsonOptions);
                if (cfg is { Profiles.Count: > 0 }) return cfg;
            }
        }
        catch (Exception ex)
        {
            Log("Не удалось прочитать конфиг: " + ex.Message);
        }

        var fresh = Presets.CreateDefault();
        Save(fresh);
        return fresh;
    }

    public static void Save(Model.AppConfig config)
    {
        try
        {
            var json = JsonSerializer.Serialize(config, JsonOptions);
            var tmp = ConfigPath + ".tmp";
            File.WriteAllText(tmp, json);
            File.Copy(tmp, ConfigPath, true);
            File.Delete(tmp);
        }
        catch (Exception ex)
        {
            Log("Не удалось сохранить конфиг: " + ex.Message);
        }
    }

    public static void Export(Model.AppConfig config, string path)
    {
        File.WriteAllText(path, JsonSerializer.Serialize(config, JsonOptions));
    }

    public static Model.AppConfig Import(string path)
    {
        var json = File.ReadAllText(path);
        var cfg = JsonSerializer.Deserialize<Model.AppConfig>(json, JsonOptions)
                  ?? throw new InvalidOperationException("Файл профиля пуст или повреждён.");
        if (cfg.Profiles.Count == 0) throw new InvalidOperationException("В файле нет ни одного профиля.");
        return cfg;
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
