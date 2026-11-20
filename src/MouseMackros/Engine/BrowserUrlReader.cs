using System.Diagnostics;
using System.Windows.Automation;

namespace MouseMackros.Engine;

/// <summary>
/// Best-effort address-bar reader for Chromium browsers and Firefox via UI
/// Automation. Returns null for non-browser windows or when the browser does
/// not expose its address bar.
/// </summary>
public static class BrowserUrlReader
{
    private static readonly string[] BrowserProcesses =
    {
        "firefox", "chrome", "msedge", "brave", "opera", "vivaldi",
        "yandex", "librewolf", "thorium", "arc"
    };

    private static readonly object Gate = new();
    private static IntPtr _lastHandle;
    private static string _lastTitle = "";
    private static string? _lastUrl;
    private static long _lastAt;
    private const int CacheMs = 800;

    public static bool IsBrowser(string processName) =>
        BrowserProcesses.Contains(processName, StringComparer.OrdinalIgnoreCase);

    public static string? TryGetUrl(WindowInfo window)
    {
        if (!IsBrowser(window.ProcessName) || window.Handle == IntPtr.Zero) return null;

        lock (Gate)
        {
            if (window.Handle == _lastHandle &&
                string.Equals(window.Title, _lastTitle, StringComparison.Ordinal) &&
                Environment.TickCount64 - _lastAt < CacheMs)
            {
                return _lastUrl;
            }
        }

        string? url = null;
        try
        {
            var root = AutomationElement.FromHandle(window.Handle);
            url = window.ProcessName.Equals("firefox", StringComparison.OrdinalIgnoreCase)
                ? ReadFirefox(root)
                : ReadChromium(root);
        }
        catch
        {
            url = null;
        }

        url = Normalize(url);

        lock (Gate)
        {
            _lastHandle = window.Handle;
            _lastTitle = window.Title;
            _lastUrl = url;
            _lastAt = Environment.TickCount64;
        }

        return url;
    }

    private static string? ReadFirefox(AutomationElement root)
    {
        var byId = root.FindFirst(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.AutomationIdProperty, "urlbar-input"));
        var value = ReadValue(byId);
        if (!string.IsNullOrWhiteSpace(value)) return value;

        return FindValueByNames(root, "поиск", "search", "адрес", "address");
    }

    private static string? ReadChromium(AutomationElement root)
    {
        var edits = root.FindAll(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));

        // Prefer the address bar by its accessible name.
        foreach (AutomationElement edit in edits)
        {
            var name = edit.Current.Name ?? "";
            if (name.Contains("адрес", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("address", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("поиск", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("search", StringComparison.OrdinalIgnoreCase))
            {
                var value = ReadValue(edit);
                if (!string.IsNullOrWhiteSpace(value)) return value;
            }
        }

        // Fallback: the first edit that already contains a URL.
        foreach (AutomationElement edit in edits)
        {
            var value = ReadValue(edit);
            if (!string.IsNullOrWhiteSpace(value) && LooksLikeUrl(value)) return value;
        }

        return FindValueByNames(root, "адрес", "address", "поиск", "search");
    }

    private static string? FindValueByNames(AutomationElement root, params string[] names)
    {
        var all = root.FindAll(TreeScope.Descendants, Condition.TrueCondition);
        foreach (AutomationElement element in all)
        {
            var name = element.Current.Name ?? "";
            if (!names.Any(n => name.Contains(n, StringComparison.OrdinalIgnoreCase))) continue;

            var value = ReadValue(element);
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }

        return null;
    }

    private static string? ReadValue(AutomationElement? element)
    {
        if (element is null) return null;
        try
        {
            if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern) &&
                pattern is ValuePattern valuePattern)
            {
                return valuePattern.Current.Value;
            }
        }
        catch
        {
            // element disappeared while reading
        }

        return null;
    }

    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = value.Trim();
        if (text.Length > 4096 || text.Contains('\n') || text.Contains('\r')) return null;
        return LooksLikeUrl(text) ? text : null;
    }

    private static bool LooksLikeUrl(string value)
    {
        if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("about:", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("file:", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("chrome:", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("edge:", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("view-source:", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return value.Contains("://", StringComparison.Ordinal) && !value.Contains(' ');
    }
}
