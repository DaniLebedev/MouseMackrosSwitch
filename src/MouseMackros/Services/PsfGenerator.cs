using System.Text;
using System.Text.RegularExpressions;

namespace MouseMackros.Services;

/// <summary>
/// Writes a ready to import .psf profile for the MBF1000PRO Mouse utility.
/// Every editable button becomes Ctrl+Alt+Shift+A..M, which MouseMackros then
/// remaps per application.
///
/// .psf is a plain UTF-16 INI file. Key values use the vendor's 3 byte encoding:
///   [type][modifier mask][key code]  (hex)
/// Type 0x70 = single key / key combination, modifiers: 1 Ctrl, 2 Shift, 4 Alt, 8 Win.
/// Key codes: A..Z = 0x04..0x1D, 1..9,0 = 0x1E..0x27 (derived from stock profiles).
///
/// The letter order follows the physical buttons, not the .psf slot order, see
/// <see cref="MouseButtonMap"/>: button 1 = Key7 = A, button 7 = Key14 = G, ...
/// </summary>
public static class PsfGenerator
{
    private const int TypeKeyboard = 0x70;
    private const int ModCtrlShiftAlt = 0x07;

    public sealed record GeneratedProfile(string Path, string ProfileName, Dictionary<int, string> ButtonTriggers);

    /// <summary>Folder where the vendor utility keeps profiles for the current user.</summary>
    public static string? FindVendorConfigDir()
    {
        try
        {
            var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var candidates = Directory.Exists(roaming)
                ? Directory.GetDirectories(roaming, "GamingMouseData*")
                : Array.Empty<string>();
            foreach (var dir in candidates)
            {
                var config = Path.Combine(dir, "config");
                if (File.Exists(Path.Combine(config, "default0.psf"))) return config;
            }

            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var fallback = Path.Combine(programFiles, "MBF1000PRO Mouse", "config");
            if (File.Exists(Path.Combine(fallback, "default0.psf"))) return fallback;
        }
        catch
        {
            // ignore
        }
        return null;
    }

    private static string? FindTemplate()
    {
        var dir = FindVendorConfigDir();
        if (dir is null) return null;
        for (int i = 0; i < 5; i++)
        {
            var candidate = Path.Combine(dir, $"default{i}.psf");
            if (File.Exists(candidate)) return candidate;
        }
        return Path.Combine(dir, "default0.psf");
    }

    public static string EncodeCombo(int modifiers, int keyCode) =>
        TypeKeyboard.ToString("X2") + modifiers.ToString("X2") + keyCode.ToString("X2");

    /// <summary>Key code for a letter A..Z in the vendor's table.</summary>
    public static int LetterCode(char letter)
    {
        char upper = char.ToUpperInvariant(letter);
        return upper is >= 'A' and <= 'Z' ? 0x04 + (upper - 'A') : 0;
    }

    /// <summary>Builds "Ctrl+Alt+Shift+A" as the vendor would store it.</summary>
    public static string EncodeLetterTrigger(char letter) =>
        EncodeCombo(ModCtrlShiftAlt, LetterCode(letter));

    /// <summary>
    /// Builds the profile from a vendor template. Without <paramref name="outputDirectory"/>
    /// the file lands in <see cref="ProfileStore.PsfDir"/> and never in the vendor folder,
    /// so nothing in the vendor configuration is modified behind the user's back.
    /// </summary>
    public static GeneratedProfile? Generate(string? outputDirectory = null, string profileName = "MouseMackros")
    {
        var templatePath = FindTemplate();
        if (templatePath is null) return null;

        var targetDir = outputDirectory ?? ProfileStore.EnsurePsfDirectory();
        Directory.CreateDirectory(targetDir);
        return WriteProfile(templatePath, Path.Combine(targetDir, "MouseMackros.psf"), profileName);
    }

    /// <summary>Builds a profile from an existing .psf template and saves it to <paramref name="targetPath"/>.</summary>
    public static GeneratedProfile? WriteProfile(string templatePath, string targetPath,
        string profileName = "MouseMackros")
    {
        if (!File.Exists(templatePath)) return null;

        var encoding = new UnicodeEncoding(false, true);
        var text = File.ReadAllText(templatePath, Encoding.Unicode);

        // Keep everything (DPI, lighting, buttons 1-3) and rewrite the button map.
        var lines = text.Replace("\r\n", "\n").Split('\n').ToList();

        SetValue(lines, "ProfileName", profileName);
        SetValue(lines, "Key0Name", "Left Button");
        SetValue(lines, "Key0Value", "100100");
        SetValue(lines, "Key1Name", "Right Button");
        SetValue(lines, "Key1Value", "100200");
        SetValue(lines, "Key2Name", "Middle Button");
        SetValue(lines, "Key2Value", "100400");

        var triggers = new Dictionary<int, string>();
        foreach (var button in MouseButtonMap.Editable)
        {
            if (button.Letter is not char letter) continue;

            SetValue(lines, $"Key{button.KeyIndex}Name", "MM:" + letter);
            SetValue(lines, $"Key{button.KeyIndex}Value", EncodeLetterTrigger(letter));
            SetValue(lines, $"MacroKey{button.KeyIndex}Name", "");

            var combo = "Ctrl+Alt+Shift+" + letter;
            triggers[button.Number] = combo;
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(targetPath));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var content = string.Join("\r\n", lines);
        File.WriteAllText(targetPath, content, encoding);

        return new GeneratedProfile(targetPath, profileName, triggers);
    }

    private static void SetValue(List<string> lines, string key, string value)
    {
        var pattern = new Regex(@"^(\s*" + Regex.Escape(key) + @"\s*=).*$", RegexOptions.IgnoreCase);
        for (int i = 0; i < lines.Count; i++)
        {
            var match = pattern.Match(lines[i]);
            if (match.Success)
            {
                lines[i] = match.Groups[1].Value + value;
                return;
            }
        }
        lines.Add(key + "=" + value);
    }
}
