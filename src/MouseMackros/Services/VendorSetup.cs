namespace MouseMackros.Services;

/// <summary>
/// Checks which .psf profile the vendor utility currently has on the mouse.
/// MouseMackros can only switch actions per window when the mouse sends the
/// neutral Ctrl+Alt+Shift+A..M layout.
/// </summary>
public static class VendorSetup
{
    public sealed record Status(string ActivePath, string ProfileName, bool UsesNeutralLayout, bool Known);

    public static Status? Detect()
    {
        try
        {
            var dir = PsfGenerator.FindVendorConfigDir();
            if (dir is null) return null;

            var configPath = Path.Combine(dir, "config.ini");
            if (!File.Exists(configPath)) return null;

            var lines = File.ReadAllLines(configPath);
            int current = 0;
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (line.StartsWith("CurrentProfile=", StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(line["CurrentProfile=".Length..], out var parsed))
                {
                    current = parsed;
                    break;
                }
            }

            string? url = null;
            var prefix = $"ProfileSlot{current}URL=";
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    url = line[prefix.Length..].Trim();
                    break;
                }
            }

            var path = string.IsNullOrWhiteSpace(url)
                ? Path.Combine(dir, $"default{current}.psf")
                : url;
            if (!File.Exists(path)) path = Path.Combine(dir, $"default{current}.psf");
            if (!File.Exists(path)) return new Status("", "", false, false);

            var psf = PsfReader.Read(path);
            if (psf is null) return new Status(path, Path.GetFileName(path), false, false);

            // Neutral layout: buttons 1-3 must send Ctrl+Alt+Shift+A..C.
            bool neutral =
                psf.ForButton(1)?.RawValue == PsfGenerator.EncodeLetterTrigger('A') &&
                psf.ForButton(2)?.RawValue == PsfGenerator.EncodeLetterTrigger('B') &&
                psf.ForButton(3)?.RawValue == PsfGenerator.EncodeLetterTrigger('C');

            var name = psf.ProfileName;
            if (string.IsNullOrWhiteSpace(name) || name.Equals("Profile1", StringComparison.OrdinalIgnoreCase))
                name = Path.GetFileNameWithoutExtension(path);

            return new Status(path, name, neutral, true);
        }
        catch (Exception ex)
        {
            ProfileStore.Log("Не удалось определить активную раскладку: " + ex.Message);
            return null;
        }
    }
}
