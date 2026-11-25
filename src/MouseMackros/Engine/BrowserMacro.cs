using System.Text.RegularExpressions;
using MouseMackros.Input;
using MouseMackros.Interop;

namespace MouseMackros.Engine;

/// <summary>
/// Browser helpers for macros. The current URL comes from UI Automation
/// (Firefox urlbar / Chromium address bar), and navigation uses Ctrl+L,
/// typing the URL and Enter in the active browser.
/// </summary>
public static class BrowserMacro
{
    private static readonly Regex PagePattern = new(
        @"([?&]page=)(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool TryChangePage(WindowInfo? window, int delta, out string error)
    {
        if (!TryCurrentUrl(window, out var url, out error)) return false;
        if (!TryReplacePage(url, delta, out var next, out error)) return false;
        Navigate(next);
        return true;
    }

    public static bool TrySetPage(WindowInfo? window, int page, out string error)
    {
        if (!TryCurrentUrl(window, out var url, out error)) return false;
        if (!TrySetPageValue(url, page, out var next, out error)) return false;
        Navigate(next);
        return true;
    }

    public static bool TryGoTo(WindowInfo? window, string url, out string error)
    {
        error = "";
        if (window is null || !BrowserUrlReader.IsBrowser(window.ProcessName))
        {
            error = "Активное окно не является браузером.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(url) ||
            (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
             !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
        {
            error = "URL должен начинаться с http:// или https://.";
            return false;
        }

        Navigate(url.Trim());
        return true;
    }

    public static bool TryReplacePage(string? url, int delta, out string result, out string error)
    {
        result = "";
        error = "";
        if (string.IsNullOrWhiteSpace(url))
        {
            error = "URL не определён.";
            return false;
        }

        var match = PagePattern.Match(url);
        if (!match.Success || !int.TryParse(match.Groups[2].Value, out int page))
        {
            error = "В URL нет параметра page=N.";
            return false;
        }

        int next = Math.Clamp(page + delta, 0, 1_000_000);
        var group = match.Groups[2];
        result = url[..group.Index] + next + url[(group.Index + group.Length)..];
        return true;
    }

    public static bool TrySetPageValue(string? url, int page, out string result, out string error)
    {
        result = "";
        error = "";
        if (string.IsNullOrWhiteSpace(url))
        {
            error = "URL не определён.";
            return false;
        }

        page = Math.Clamp(page, 0, 1_000_000);
        var match = PagePattern.Match(url);
        if (match.Success)
        {
            var group = match.Groups[2];
            result = url[..group.Index] + page + url[(group.Index + group.Length)..];
            return true;
        }

        int hash = url.IndexOf('#');
        var head = hash >= 0 ? url[..hash] : url;
        var tail = hash >= 0 ? url[hash..] : "";
        var separator = head.Contains('?') ? "&" : "?";
        result = head + separator + "page=" + page + tail;
        return true;
    }

    public static bool TrySetQuery(WindowInfo? window, string name, string value, out string error)
    {
        if (!TryCurrentUrl(window, out var url, out error)) return false;
        if (!TrySetQueryValue(url, name, value, out var next, out error)) return false;
        Navigate(next);
        return true;
    }

    public static bool TryChangeQuery(WindowInfo? window, string name, int delta, out string error)
    {
        if (!TryCurrentUrl(window, out var url, out error)) return false;
        if (!TryChangeQueryValue(url, name, delta, out var next, out error)) return false;
        Navigate(next);
        return true;
    }

    public static bool TrySetPath(WindowInfo? window, int index, string value, out string error)
    {
        if (!TryCurrentUrl(window, out var url, out error)) return false;
        if (!TrySetPathValue(url, index, value, out var next, out error)) return false;
        Navigate(next);
        return true;
    }

    public static bool TryChangePath(WindowInfo? window, int index, int delta, out string error)
    {
        if (!TryCurrentUrl(window, out var url, out error)) return false;
        if (!TryChangePathValue(url, index, delta, out var next, out error)) return false;
        Navigate(next);
        return true;
    }

    public static bool TryNewTab(WindowInfo? window, string? url, int position, out string error)
    {
        if (!TryBrowserWindow(window, out error)) return false;

        // Browsers open a new tab right after the current one, so selecting
        // the tab before the requested position puts the new tab in place.
        // Position 1 cannot be inserted before the first tab with the keyboard.
        if (position is >= 2 and <= 8)
        {
            InputSender.Combo(new[] { NativeMethods.VK_CONTROL }, 0x30 + position - 1);
            Thread.Sleep(80);
        }
        else if (position > 8)
        {
            InputSender.Combo(new[] { NativeMethods.VK_CONTROL }, 0x39);
            Thread.Sleep(80);
        }

        InputSender.Combo(new[] { NativeMethods.VK_CONTROL }, 0x54);
        Thread.Sleep(100);

        if (!string.IsNullOrWhiteSpace(url))
            Navigate(url.Trim());

        return true;
    }

    public static bool TryCloseTab(WindowInfo? window, int position, out string error)
    {
        if (!TryBrowserWindow(window, out error)) return false;

        if (position >= 1)
        {
            int key = position <= 8 ? 0x30 + position : 0x39;
            InputSender.Combo(new[] { NativeMethods.VK_CONTROL }, key);
            Thread.Sleep(80);
        }

        InputSender.Combo(new[] { NativeMethods.VK_CONTROL }, 0x57);
        Thread.Sleep(80);
        return true;
    }

    public static bool TryReload(WindowInfo? window, out string error)
    {
        if (!TryBrowserWindow(window, out error)) return false;
        InputSender.Combo(Array.Empty<int>(), 0x74);
        Thread.Sleep(80);
        return true;
    }

    public static bool TrySetQueryValue(string? url, string name, string value,
        out string result, out string error)
    {
        result = "";
        error = "";
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(name))
        {
            error = "URL или имя параметра пусты.";
            return false;
        }

        var encoded = Uri.EscapeDataString(value ?? "");
        var pattern = new Regex("([?&])" + Regex.Escape(name) + "=([^&#]*)",
            RegexOptions.IgnoreCase);
        var match = pattern.Match(url);
        if (match.Success)
        {
            var group = match.Groups[2];
            result = url[..group.Index] + encoded + url[(group.Index + group.Length)..];
            return true;
        }

        int hash = url.IndexOf('#');
        var head = hash >= 0 ? url[..hash] : url;
        var tail = hash >= 0 ? url[hash..] : "";
        var separator = head.Contains('?') ? "&" : "?";
        result = head + separator + name + "=" + encoded + tail;
        return true;
    }

    public static bool TryChangeQueryValue(string? url, string name, int delta,
        out string result, out string error)
    {
        result = "";
        error = "";
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(name))
        {
            error = "URL или имя параметра пусты.";
            return false;
        }

        var pattern = new Regex("([?&])" + Regex.Escape(name) + "=([^&#]*)",
            RegexOptions.IgnoreCase);
        var match = pattern.Match(url);
        if (!match.Success || !int.TryParse(match.Groups[2].Value, out int current))
        {
            error = $"Параметр «{name}» не найден или не является числом.";
            return false;
        }

        long next = Math.Clamp((long)current + delta, 0, int.MaxValue);
        var group = match.Groups[2];
        result = url[..group.Index] + next + url[(group.Index + group.Length)..];
        return true;
    }

    public static bool TrySetPathValue(string? url, int index, string value,
        out string result, out string error)
    {
        if (!TryGetPath(url, out var pathStart, out var pathEnd, out var segments, out error))
        {
            result = "";
            return false;
        }
        if (index < 1 || index > segments.Count)
        {
            result = "";
            error = $"В URL нет сегмента пути №{index}.";
            return false;
        }

        var copy = segments.ToList();
        copy[index - 1] = Uri.EscapeDataString(value ?? "");
        var path = "/" + string.Join("/", copy);
        if (url![pathStart..pathEnd].EndsWith("/") && path.Length > 1) path += "/";
        result = url[..pathStart] + path + url[pathEnd..];
        return true;
    }

    public static bool TryChangePathValue(string? url, int index, int delta,
        out string result, out string error)
    {
        if (!TryGetPath(url, out var pathStart, out var pathEnd, out var segments, out error))
        {
            result = "";
            return false;
        }
        if (index < 1 || index > segments.Count)
        {
            result = "";
            error = $"В URL нет сегмента пути №{index}.";
            return false;
        }

        if (!int.TryParse(Uri.UnescapeDataString(segments[index - 1]), out int current))
        {
            result = "";
            error = $"Сегмент пути №{index} не является числом.";
            return false;
        }

        var copy = segments.ToList();
        copy[index - 1] = Math.Clamp((long)current + delta, 0, int.MaxValue).ToString();
        var path = "/" + string.Join("/", copy);
        if (url![pathStart..pathEnd].EndsWith("/") && path.Length > 1) path += "/";
        result = url[..pathStart] + path + url[pathEnd..];
        return true;
    }

    private static bool TryGetPath(string? url, out int pathStart, out int pathEnd,
        out List<string> segments, out string error)
    {
        pathStart = pathEnd = 0;
        segments = new List<string>();
        error = "";
        if (string.IsNullOrWhiteSpace(url))
        {
            error = "URL не определён.";
            return false;
        }

        int scheme = url.IndexOf("://", StringComparison.Ordinal);
        if (scheme < 0)
        {
            error = "URL не содержит схему.";
            return false;
        }

        pathStart = url.IndexOf('/', scheme + 3);
        if (pathStart < 0)
        {
            error = "В URL нет пути.";
            return false;
        }

        pathEnd = url.IndexOfAny(new[] { '?', '#' }, pathStart);
        if (pathEnd < 0) pathEnd = url.Length;
        segments = url[pathStart..pathEnd]
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .ToList();
        return true;
    }

    private static bool TryBrowserWindow(WindowInfo? window, out string error)
    {
        if (window is null || !BrowserUrlReader.IsBrowser(window.ProcessName))
        {
            error = "Активное окно не является браузером.";
            return false;
        }
        error = "";
        return true;
    }

    private static bool TryCurrentUrl(WindowInfo? window, out string url, out string error)
    {
        url = "";
        error = "";
        if (window is null)
        {
            error = "Активное окно не определено.";
            return false;
        }

        if (!BrowserUrlReader.IsBrowser(window.ProcessName))
        {
            error = "Активное окно не является браузером.";
            return false;
        }

        url = BrowserUrlReader.TryGetUrl(window) ?? window.Url ?? "";
        if (string.IsNullOrWhiteSpace(url))
        {
            error = "Не удалось прочитать URL из адресной строки.";
            return false;
        }

        return true;
    }

    private static void Navigate(string url)
    {
        InputSender.ReleaseHeldModifiers();

        // Ctrl+L focuses/selects the address bar in Firefox, Chrome and Edge.
        InputSender.Combo(new[] { NativeMethods.VK_CONTROL }, 0x4C);
        Thread.Sleep(90);

        foreach (var ch in url)
        {
            InputSender.SendUnicode(ch, true);
            InputSender.SendUnicode(ch, false);
            Thread.Sleep(2);
        }

        InputSender.KeyDown(0x0D);
        InputSender.KeyUp(0x0D);
        Thread.Sleep(150);
    }
}
