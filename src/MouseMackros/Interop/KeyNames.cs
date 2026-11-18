using System.Text;

namespace MouseMackros.Interop;

/// <summary>Virtual key names, parsing ("Ctrl+Alt+Left") and formatting.</summary>
public static class KeyNames
{
    public const string Ctrl = "Ctrl";
    public const string Shift = "Shift";
    public const string Alt = "Alt";
    public const string Win = "Win";

    private static readonly Dictionary<string, int> NameToVk = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Backspace"] = 0x08, ["Tab"] = 0x09, ["Enter"] = 0x0D, ["Return"] = 0x0D,
        ["Pause"] = 0x13, ["CapsLock"] = 0x14, ["Esc"] = 0x1B, ["Escape"] = 0x1B,
        ["Space"] = 0x20, ["PgUp"] = 0x21, ["PageUp"] = 0x21, ["PgDn"] = 0x22, ["PageDown"] = 0x22,
        ["End"] = 0x23, ["Home"] = 0x24, ["Left"] = 0x25, ["Up"] = 0x26, ["Right"] = 0x27, ["Down"] = 0x28,
        ["PrintScreen"] = 0x2C, ["Insert"] = 0x2D, ["Ins"] = 0x2D, ["Delete"] = 0x2E, ["Del"] = 0x2E,
        ["Help"] = 0x2F, ["NumLock"] = 0x90, ["ScrollLock"] = 0x91,
        ["Num*"] = 0x6A, ["Num+"] = 0x6B, ["Num-"] = 0x6D, ["Num."] = 0x6E, ["Num/"] = 0x6F,
        ["BrowserBack"] = 0xA6, ["BrowserForward"] = 0xA7, ["BrowserRefresh"] = 0xA8,
        ["BrowserStop"] = 0xA9, ["BrowserSearch"] = 0xAA, ["BrowserFavorites"] = 0xAB, ["BrowserHome"] = 0xAC,
        ["VolumeMute"] = 0xAD, ["VolumeDown"] = 0xAE, ["VolumeUp"] = 0xAF,
        ["MediaNext"] = 0xB0, ["MediaPrev"] = 0xB1, ["MediaStop"] = 0xB2, ["MediaPlayPause"] = 0xB3,
        ["LaunchMail"] = 0xB4, ["LaunchMedia"] = 0xB5, ["LaunchApp1"] = 0xB6, ["LaunchApp2"] = 0xB7,
        ["Oem;"] = 0xBA, ["Oem="] = 0xBB, ["Oem,"] = 0xBC, ["Oem-"] = 0xBD, ["Oem."] = 0xBE,
        ["Oem/"] = 0xBF, ["Oem`"] = 0xC0, ["Oem["] = 0xDB, ["Oem\\"] = 0xDC, ["Oem]"] = 0xDD, ["Oem'"] = 0xDE,
    };

    private static readonly Dictionary<int, string> VkToName = new()
    {
        [0x08] = "Backspace", [0x09] = "Tab", [0x0D] = "Enter", [0x13] = "Pause", [0x14] = "CapsLock",
        [0x1B] = "Esc", [0x20] = "Space", [0x21] = "PgUp", [0x22] = "PgDn", [0x23] = "End", [0x24] = "Home",
        [0x25] = "Left", [0x26] = "Up", [0x27] = "Right", [0x28] = "Down", [0x2C] = "PrintScreen",
        [0x2D] = "Insert", [0x2E] = "Delete", [0x2F] = "Help", [0x90] = "NumLock", [0x91] = "ScrollLock",
        [0x6A] = "Num*", [0x6B] = "Num+", [0x6D] = "Num-", [0x6E] = "Num.", [0x6F] = "Num/",
        [0xA6] = "BrowserBack", [0xA7] = "BrowserForward", [0xA8] = "BrowserRefresh", [0xA9] = "BrowserStop",
        [0xAA] = "BrowserSearch", [0xAB] = "BrowserFavorites", [0xAC] = "BrowserHome",
        [0xAD] = "VolumeMute", [0xAE] = "VolumeDown", [0xAF] = "VolumeUp",
        [0xB0] = "MediaNext", [0xB1] = "MediaPrev", [0xB2] = "MediaStop", [0xB3] = "MediaPlayPause",
        [0xB4] = "LaunchMail", [0xB5] = "LaunchMedia", [0xB6] = "LaunchApp1", [0xB7] = "LaunchApp2",
        [0xBA] = "Oem;", [0xBB] = "Oem=", [0xBC] = "Oem,", [0xBD] = "Oem-", [0xBE] = "Oem.",
        [0xBF] = "Oem/", [0xC0] = "Oem`", [0xDB] = "Oem[", [0xDC] = "Oem\\", [0xDD] = "Oem]", [0xDE] = "Oem'",
        [0x5B] = "LWin", [0x5C] = "RWin",
    };

    static KeyNames()
    {
        for (int i = 0; i < 26; i++)
        {
            var name = ((char)('A' + i)).ToString();
            NameToVk[name] = 0x41 + i;
            VkToName[0x41 + i] = name;
        }
        for (int i = 0; i <= 9; i++)
        {
            NameToVk[i.ToString()] = 0x30 + i;
            VkToName[0x30 + i] = i.ToString();
        }
        for (int i = 1; i <= 24; i++)
        {
            NameToVk["F" + i] = 0x6F + i; // F1 = 0x70
            VkToName[0x6F + i] = "F" + i;
        }
        for (int i = 0; i <= 9; i++)
        {
            NameToVk["Num" + i] = 0x60 + i;
            VkToName[0x60 + i] = "Num" + i;
        }
    }

    public static bool TryGetVk(string name, out int vk) => NameToVk.TryGetValue(name.Trim(), out vk);

    public static string GetName(int vk)
    {
        if (VkToName.TryGetValue(vk, out var n)) return n;
        return "VK" + vk.ToString("X2");
    }

    public static bool IsModifier(int vk) => vk is NativeMethods.VK_SHIFT or NativeMethods.VK_CONTROL or NativeMethods.VK_MENU
        or NativeMethods.VK_LWIN or NativeMethods.VK_RWIN
        or NativeMethods.VK_LSHIFT or NativeMethods.VK_RSHIFT
        or NativeMethods.VK_LCONTROL or NativeMethods.VK_RCONTROL
        or NativeMethods.VK_LMENU or NativeMethods.VK_RMENU;

    public static bool IsExtended(int vk) => vk is 0x21 or 0x22 or 0x23 or 0x24 or 0x25 or 0x26 or 0x27 or 0x28
        or 0x2D or 0x2E or 0x90 or 0x6F or 0xA3 or 0xA5 or 0x5C or 0x5B or 0x2C;

    /// <summary>Modifier vk list from a parsed combination, e.g. "Ctrl+Alt+A" -&gt; [0x11,0x12], 'A'.</summary>
    public static bool TryParseCombo(string text, out List<int> modifiers, out int key)
    {
        modifiers = new List<int>();
        key = 0;
        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return false;
        foreach (var raw in parts)
        {
            var p = raw;
            switch (p.ToLowerInvariant())
            {
                case "ctrl": case "control": modifiers.Add(NativeMethods.VK_CONTROL); continue;
                case "shift": modifiers.Add(NativeMethods.VK_SHIFT); continue;
                case "alt": modifiers.Add(NativeMethods.VK_MENU); continue;
                case "win": case "super": case "meta": modifiers.Add(NativeMethods.VK_LWIN); continue;
            }
            if (!TryGetVk(p, out key)) return false;
        }
        return key != 0;
    }

    public static string NormalizeCombo(string text) =>
        TryParseCombo(text, out var mods, out var key) ? FormatCombo(mods, key) : text.Trim();

    /// <summary>Formats a combination from virtual-key codes, e.g. [0x11, 0x12], 0x7B.</summary>
    public static string FormatCombo(IEnumerable<int> modifiers, int key) =>
        FormatNormalized(modifiers.Select(NormalizeModifier), key);

    /// <summary>Formats a combination from normalized modifiers (0 = Ctrl, 1 = Shift, 2 = Alt, 3 = Win).</summary>
    public static string FormatNormalized(IEnumerable<int> modifiers, int key)
    {
        var sb = new StringBuilder();
        var set = modifiers.Distinct().OrderBy(m => m).ToList();
        foreach (var m in set)
        {
            sb.Append(m switch
            {
                0 => Ctrl,
                1 => Shift,
                2 => Alt,
                3 => Win,
                _ => ""
            });
            sb.Append('+');
        }
        sb.Append(GetName(key));
        return sb.ToString();
    }

    /// <summary>0 = Ctrl, 1 = Shift, 2 = Alt, 3 = Win.</summary>
    public static int NormalizeModifier(int vk) => vk switch
    {
        NativeMethods.VK_CONTROL or NativeMethods.VK_LCONTROL or NativeMethods.VK_RCONTROL => 0,
        NativeMethods.VK_SHIFT or NativeMethods.VK_LSHIFT or NativeMethods.VK_RSHIFT => 1,
        NativeMethods.VK_MENU or NativeMethods.VK_LMENU or NativeMethods.VK_RMENU => 2,
        NativeMethods.VK_LWIN or NativeMethods.VK_RWIN => 3,
        _ => 0
    };

    public static int ModifierToVk(int normalized) => normalized switch
    {
        0 => NativeMethods.VK_CONTROL,
        1 => NativeMethods.VK_SHIFT,
        2 => NativeMethods.VK_MENU,
        _ => NativeMethods.VK_LWIN
    };

    private static readonly Dictionary<string, string> NicePieces = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Left"] = "←", ["Right"] = "→", ["Up"] = "↑", ["Down"] = "↓",
        ["Esc"] = "Esc", ["Space"] = "Пробел", ["Enter"] = "Enter", ["Backspace"] = "Backspace",
        ["PgUp"] = "Page Up", ["PgDn"] = "Page Down", ["Insert"] = "Insert", ["Delete"] = "Delete",
        ["CapsLock"] = "Caps Lock", ["NumLock"] = "Num Lock", ["PrintScreen"] = "Print Screen",
        ["VolumeUp"] = "Громкость +", ["VolumeDown"] = "Громкость −", ["VolumeMute"] = "Без звука",
        ["MediaPlayPause"] = "Play/Pause", ["MediaNext"] = "Следующий трек", ["MediaPrev"] = "Предыдущий трек",
        ["MediaStop"] = "Стоп", ["Oem;"] = ";", ["Oem="] = "=", ["Oem,"] = ",", ["Oem-"] = "-",
        ["Oem."] = ".", ["Oem/"] = "/", ["Oem`"] = "`", ["Oem["] = "[", ["Oem\\"] = "\\",
        ["Oem]"] = "]", ["Oem'"] = "'"
    };

    /// <summary>Human friendly version of a combination, for the UI.</summary>
    public static string DescribeCombo(string combo)
    {
        if (string.IsNullOrWhiteSpace(combo)) return "—";
        var parts = combo.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return combo;
        for (int i = 0; i < parts.Length; i++)
        {
            var p = parts[i];
            if (i == parts.Length - 1)
            {
                if (NicePieces.TryGetValue(p, out var nice)) parts[i] = nice;
            }
            else if (p.Equals("Win", StringComparison.OrdinalIgnoreCase))
            {
                parts[i] = "Win";
            }
        }
        return string.Join(" + ", parts);
    }

    public static string DescribeMouse(int vk)
    {
        return vk switch
        {
            NativeMethods.VK_LBUTTON => "ЛКМ",
            NativeMethods.VK_RBUTTON => "ПКМ",
            NativeMethods.VK_MBUTTON => "СКМ",
            NativeMethods.VK_XBUTTON1 => "Боковая 1",
            NativeMethods.VK_XBUTTON2 => "Боковая 2",
            _ => GetName(vk)
        };
    }
}
