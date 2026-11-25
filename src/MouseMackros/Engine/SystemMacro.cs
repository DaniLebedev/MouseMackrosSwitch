using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using MouseMackros.Interop;

namespace MouseMackros.Engine;

/// <summary>
/// Small system helpers used by macros: clipboard, windows, media keys,
/// launching programs and a few Windows actions.
/// </summary>
public static class SystemMacro
{
    public static bool TrySetClipboard(string text, out string error)
    {
        error = "";
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    System.Windows.Clipboard.SetText(text ?? "");
                    failure = null;
                    return;
                }
                catch (Exception ex)
                {
                    failure = ex;
                    Thread.Sleep(40);
                }
            }
        })
        {
            IsBackground = true
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(2500);

        if (thread.IsAlive)
        {
            error = "Буфер обмена занят другим приложением.";
            return false;
        }
        if (failure is not null)
        {
            error = "Не удалось записать буфер обмена: " + failure.Message;
            return false;
        }
        return true;
    }

    public static string GetClipboardText()
    {
        string result = "";
        var thread = new Thread(() =>
        {
            try
            {
                if (System.Windows.Clipboard.ContainsText())
                    result = System.Windows.Clipboard.GetText();
            }
            catch
            {
                // clipboard is busy or contains a non-text format
            }
        })
        {
            IsBackground = true
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(1500);
        return result;
    }

    public static bool TryPasteClipboard(out string error)
    {
        error = "";
        InputSender.Combo(new[] { NativeMethods.VK_CONTROL }, 0x56);
        return true;
    }

    public static bool TryRun(string fileName, string? arguments, out string error)
    {
        error = "";
        if (string.IsNullOrWhiteSpace(fileName))
        {
            error = "Не указана программа.";
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments ?? "",
                UseShellExecute = true
            });
            return true;
        }
        catch (Exception ex)
        {
            error = "Не удалось запустить: " + ex.Message;
            return false;
        }
    }

    public static bool TryOpen(string path, out string error)
    {
        error = "";
        if (string.IsNullOrWhiteSpace(path))
        {
            error = "Не указан путь или ссылка.";
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            error = "Не удалось открыть: " + ex.Message;
            return false;
        }
    }

    public static bool TryActivateWindow(string title, out string error)
    {
        error = "";
        if (string.IsNullOrWhiteSpace(title))
        {
            error = "Не указан заголовок окна.";
            return false;
        }

        IntPtr found = IntPtr.Zero;
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (!NativeMethods.IsWindowVisible(hwnd)) return true;
            var text = WindowTitle(hwnd);
            if (text.Length > 0 && text.Contains(title, StringComparison.OrdinalIgnoreCase))
            {
                found = hwnd;
                return false;
            }
            return true;
        }, IntPtr.Zero);

        if (found == IntPtr.Zero)
        {
            error = $"Окно «{title}» не найдено.";
            return false;
        }

        NativeMethods.ShowWindow(found, NativeMethods.SW_RESTORE);
        if (!NativeMethods.SetForegroundWindow(found))
        {
            error = $"Windows не разрешила переключиться на «{title}».";
            return false;
        }
        return true;
    }

    public static bool TryWindowState(string action, out string error)
    {
        error = "";
        var hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
            error = "Нет активного окна.";
            return false;
        }

        switch (action)
        {
            case "minimize":
                NativeMethods.ShowWindow(hwnd, 6); // SW_MINIMIZE
                return true;
            case "maximize":
                NativeMethods.ShowWindow(hwnd, 3); // SW_MAXIMIZE
                return true;
            case "restore":
                NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
                return true;
            case "close":
                NativeMethods.PostMessage(hwnd, 0x0010, IntPtr.Zero, IntPtr.Zero); // WM_CLOSE
                return true;
            default:
                error = "Неизвестное действие с окном: " + action;
                return false;
        }
    }

    public static bool TryVolume(int delta, out string error)
    {
        error = "";
        int vk = delta >= 0 ? 0xAF : 0xAE; // VolumeUp / VolumeDown
        int count = Math.Clamp(Math.Abs(delta), 1, 100);
        for (int i = 0; i < count; i++)
        {
            InputSender.Combo(Array.Empty<int>(), vk);
            Thread.Sleep(25);
        }
        return true;
    }

    public static bool TryMute(out string error)
    {
        error = "";
        InputSender.Combo(Array.Empty<int>(), 0xAD);
        return true;
    }

    public static bool TryMedia(string action, out string error)
    {
        error = "";
        int vk = action.ToLowerInvariant() switch
        {
            "next" or "forward" => 0xB0,
            "prev" or "previous" or "back" => 0xB1,
            "stop" => 0xB2,
            "play" or "pause" or "playpause" or "toggle" => 0xB3,
            _ => 0
        };

        if (vk == 0)
        {
            error = "Неизвестная медиа-команда: " + action;
            return false;
        }

        InputSender.Combo(Array.Empty<int>(), vk);
        return true;
    }

    public static bool TryScreenshot(out string error)
    {
        error = "";
        // The standard Windows snipping overlay.
        InputSender.Combo(new[] { NativeMethods.VK_LWIN, NativeMethods.VK_SHIFT }, 0x53);
        return true;
    }

    public static bool TryLock(out string error)
    {
        error = "";
        if (NativeMethods.LockWorkStation()) return true;
        error = "Windows не разрешила заблокировать сеанс.";
        return false;
    }

    public static bool TrySleep(out string error)
    {
        error = "";
        if (NativeMethods.SetSuspendState(false, false, false)) return true;
        error = "Windows не разрешила перевести компьютер в сон.";
        return false;
    }

    public static bool TryGetQueryParam(string? url, string name, out string value)
    {
        value = "";
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(name)) return false;

        var match = Regex.Match(url, "([?&])" + Regex.Escape(name) + "=([^&#]*)",
            RegexOptions.IgnoreCase);
        if (!match.Success) return false;
        value = Uri.UnescapeDataString(match.Groups[2].Value);
        return true;
    }

    private static string WindowTitle(IntPtr hwnd)
    {
        var length = NativeMethods.GetWindowTextLength(hwnd);
        if (length <= 0) return "";
        var buffer = new System.Text.StringBuilder(length + 1);
        NativeMethods.GetWindowText(hwnd, buffer, buffer.Capacity);
        return buffer.ToString();
    }
}
