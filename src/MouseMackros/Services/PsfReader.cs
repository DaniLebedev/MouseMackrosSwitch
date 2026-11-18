using System.Text;
using MouseMackros.Input;
using MouseMackros.Interop;

namespace MouseMackros.Services;

/// <summary>One [KeyN] entry decoded from a .psf file.</summary>
public sealed record PsfKey(int KeyIndex, string Name, string RawValue, string? Trigger, string Display);

/// <summary>Decoded .psf profile: what each mouse button is set to in the vendor app.</summary>
public sealed class PsfFile
{
    public required string Path { get; init; }
    public required IReadOnlyList<PsfKey> Keys { get; init; }
    public string ProfileName { get; init; } = "";

    public PsfKey? ForButton(int button)
    {
        var info = MouseButtonMap.Get(button);
        return info is null ? null : Keys.FirstOrDefault(k => k.KeyIndex == info.KeyIndex);
    }

    public PsfKey? ForKeyIndex(int keyIndex) => Keys.FirstOrDefault(k => k.KeyIndex == keyIndex);
}

/// <summary>
/// Reads .psf files written by the MBF1000PRO utility. Used when a profile
/// points to a specific vendor layout: the app can show which action every
/// button sends and pre-fill triggers.
/// </summary>
public static class PsfReader
{
    private const int TypeKeyboard = 0x70;
    private const int TypeMouse = 0x10;

    public static PsfFile? Read(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;

            var text = File.ReadAllText(path, DetectEncoding(path));
            var name = "";
            var keys = new List<PsfKey>();

            foreach (var rawLine in text.Split('\n'))
            {
                var line = rawLine.Trim();
                var eq = line.IndexOf('=');
                if (eq <= 0) continue;

                var key = line[..eq].Trim();
                var value = line[(eq + 1)..].Trim();

                if (key.Equals("ProfileName", StringComparison.OrdinalIgnoreCase))
                {
                    name = value;
                    continue;
                }

                if (!key.StartsWith("Key", StringComparison.OrdinalIgnoreCase)) continue;
                if (!key.EndsWith("Value", StringComparison.OrdinalIgnoreCase)) continue;

                var middle = key[3..^5];
                if (!int.TryParse(middle, out int index)) continue;

                keys.Add(Decode(index, value));
            }

            return new PsfFile { Path = path, Keys = keys, ProfileName = name };
        }
        catch (Exception ex)
        {
            ProfileStore.Log($"Не удалось прочитать .psf {path}: {ex.Message}");
            return null;
        }
    }

    private static PsfKey Decode(int index, string raw)
    {
        var name = "Key" + index;
        if (!TryParseHex(raw, out int type, out int modifiers, out int code))
            return new PsfKey(index, name, raw, null, "не задано");

        if (type == TypeKeyboard)
        {
            int vk = HidToVk(code);
            if (vk == 0) return new PsfKey(index, name, raw, null, "клавиша " + code.ToString("X2"));

            var normalized = NormalizeModifiers(modifiers);
            var combo = KeyNames.FormatNormalized(normalized, vk);
            var trigger = Trigger.Key(combo).ToString();
            return new PsfKey(index, name, raw, trigger, KeyNames.DescribeCombo(combo));
        }

        if (type == TypeMouse)
        {
            // Mouse entries put the button code in the middle byte: 100100 = Left,
            // 100200 = Right, 100400 = Middle.
            var trigger = MouseCodeToTrigger(modifiers);
            return new PsfKey(index, name, raw, trigger,
                trigger is null ? "кнопка мыши" : Trigger.Parse(trigger).DisplayName);
        }

        return new PsfKey(index, name, raw, null, "другое действие");
    }

    private static bool TryParseHex(string raw, out int type, out int modifiers, out int code)
    {
        type = modifiers = code = 0;
        var value = raw.Trim();
        if (value.Length != 6) return false;
        try
        {
            type = Convert.ToInt32(value[..2], 16);
            modifiers = Convert.ToInt32(value[2..4], 16);
            code = Convert.ToInt32(value[4..], 16);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Vendor modifier mask (1 Ctrl, 2 Shift, 4 Alt, 8 Win) to normalized order (0..3).</summary>
    private static IEnumerable<int> NormalizeModifiers(int mask)
    {
        if ((mask & 1) != 0) yield return 0;
        if ((mask & 2) != 0) yield return 1;
        if ((mask & 4) != 0) yield return 2;
        if ((mask & 8) != 0) yield return 3;
    }

    private static string? MouseCodeToTrigger(int code) => code switch
    {
        0x01 => "mouse:Left",
        0x02 => "mouse:Right",
        0x04 => "mouse:Middle",
        0x08 => "mouse:X1",
        0x10 => "mouse:X2",
        _ => null
    };

    /// <summary>USB HID usage id -&gt; Windows virtual key for the keys we can decode.</summary>
    public static int HidToVk(int code)
    {
        if (code is >= 0x04 and <= 0x1D) return 0x41 + (code - 0x04);      // A..Z
        if (code is >= 0x1E and <= 0x26) return 0x31 + (code - 0x1E);      // 1..9
        if (code == 0x27) return 0x30;                                     // 0

        return code switch
        {
            0x28 => 0x0D, // Enter
            0x29 => 0x1B, // Esc
            0x2A => 0x08, // Backspace
            0x2B => 0x09, // Tab
            0x2C => 0x20, // Space
            0x2D => 0xBD, // -
            0x2E => 0xBB, // =
            0x2F => 0xDB, // [
            0x30 => 0xDD, // ]
            0x31 => 0xDC, // backslash
            0x33 => 0xBA, // ;
            0x34 => 0xDE, // '
            0x35 => 0xC0, // `
            0x36 => 0xBC, // ,
            0x37 => 0xBE, // .
            0x38 => 0xBF, // /
            0x39 => 0x14, // CapsLock
            0x46 => 0x2C, // PrintScreen
            0x47 => 0x91, // ScrollLock
            0x48 => 0x13, // Pause
            0x49 => 0x2D, // Insert
            0x4A => 0x24, // Home
            0x4B => 0x21, // PgUp
            0x4C => 0x2E, // Delete
            0x4D => 0x23, // End
            0x4E => 0x22, // PgDn
            0x4F => 0x27, // Right
            0x50 => 0x25, // Left
            0x51 => 0x28, // Down
            0x52 => 0x26, // Up
            0x53 => 0x90, // NumLock
            _ when code is >= 0x3A and <= 0x45 => 0x70 + (code - 0x3A), // F1..F12
            _ => 0
        };
    }

    private static Encoding DetectEncoding(string path)
    {
        using var stream = File.OpenRead(path);
        var head = new byte[2];
        int read = stream.Read(head, 0, 2);
        if (read == 2 && head[0] == 0xFF && head[1] == 0xFE) return Encoding.Unicode;
        if (read == 2 && head[0] == 0xFE && head[1] == 0xFF) return Encoding.BigEndianUnicode;
        return new UTF8Encoding(false);
    }
}
