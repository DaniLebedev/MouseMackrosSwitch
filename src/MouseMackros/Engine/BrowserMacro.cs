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
