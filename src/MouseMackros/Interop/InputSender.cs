using System.Runtime.InteropServices;

namespace MouseMackros.Interop;

/// <summary>
/// Sends keyboard / mouse input to the system. Every event is tagged with a magic
/// dwExtraInfo value so that our own low level hooks can ignore it and avoid loops.
/// </summary>
internal static class InputSender
{
    public static readonly IntPtr Magic = new(unchecked((nint)0x4D4D5357)); // "MMSW"

    private static readonly object Gate = new();

    public static void KeyDown(int vk) => SendKey(vk, true);
    public static void KeyUp(int vk) => SendKey(vk, false);

    public static void SendKey(int vk, bool down)
    {
        var scan = (ushort)NativeMethods.MapVirtualKey((uint)vk, NativeMethods.MAPVK_VK_TO_VSC);
        bool extended = KeyNames.IsExtended(vk);
        var input = new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_KEYBOARD,
            u = new NativeMethods.InputUnion
            {
                ki = new NativeMethods.KEYBDINPUT
                {
                    wVk = (ushort)vk,
                    wScan = scan,
                    dwFlags = (down ? 0u : NativeMethods.KEYEVENTF_KEYUP) |
                              (extended ? NativeMethods.KEYEVENTF_EXTENDEDKEY : 0u),
                    dwExtraInfo = Magic
                }
            }
        };
        Send(input);
    }

    public static void SendUnicode(char ch, bool down)
    {
        var input = new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_KEYBOARD,
            u = new NativeMethods.InputUnion
            {
                ki = new NativeMethods.KEYBDINPUT
                {
                    wVk = 0,
                    wScan = ch,
                    dwFlags = NativeMethods.KEYEVENTF_UNICODE | (down ? 0u : NativeMethods.KEYEVENTF_KEYUP),
                    dwExtraInfo = Magic
                }
            }
        };
        Send(input);
    }

    public static void MouseButton(string button, bool down)
    {
        uint flag;
        uint data = 0;
        switch (button)
        {
            case "left": flag = down ? NativeMethods.MOUSEEVENTF_LEFTDOWN : NativeMethods.MOUSEEVENTF_LEFTUP; break;
            case "right": flag = down ? NativeMethods.MOUSEEVENTF_RIGHTDOWN : NativeMethods.MOUSEEVENTF_RIGHTUP; break;
            case "middle": flag = down ? NativeMethods.MOUSEEVENTF_MIDDLEDOWN : NativeMethods.MOUSEEVENTF_MIDDLEUP; break;
            case "x1": flag = down ? NativeMethods.MOUSEEVENTF_XDOWN : NativeMethods.MOUSEEVENTF_XUP; data = 1; break;
            case "x2": flag = down ? NativeMethods.MOUSEEVENTF_XDOWN : NativeMethods.MOUSEEVENTF_XUP; data = 2; break;
            default: return;
        }
        var input = new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_MOUSE,
            u = new NativeMethods.InputUnion
            {
                mi = new NativeMethods.MOUSEINPUT
                {
                    mouseData = data,
                    dwFlags = flag,
                    dwExtraInfo = Magic
                }
            }
        };
        Send(input);
    }

    public static void Wheel(int ticks, bool horizontal = false)
    {
        var input = new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_MOUSE,
            u = new NativeMethods.InputUnion
            {
                mi = new NativeMethods.MOUSEINPUT
                {
                    mouseData = unchecked((uint)(ticks * 120)),
                    dwFlags = horizontal ? NativeMethods.MOUSEEVENTF_HWHEEL : NativeMethods.MOUSEEVENTF_WHEEL,
                    dwExtraInfo = Magic
                }
            }
        };
        Send(input);
    }

    /// <summary>
    /// Releases modifiers the user is physically holding so that an injected
    /// combination is not polluted by them (e.g. trigger Ctrl+Alt+Shift+A -&gt; action Alt+Left).
    /// </summary>
    public static void ReleaseHeldModifiers()
    {
        foreach (var vk in new[]
                 {
                     NativeMethods.VK_LSHIFT, NativeMethods.VK_RSHIFT,
                     NativeMethods.VK_LCONTROL, NativeMethods.VK_RCONTROL,
                     NativeMethods.VK_LMENU, NativeMethods.VK_RMENU,
                     NativeMethods.VK_LWIN, NativeMethods.VK_RWIN
                 })
        {
            if ((NativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0)
            {
                SendKey(vk, false);
            }
        }
    }

    public static void Combo(IEnumerable<int> modifiers, int key)
    {
        var mods = modifiers.Distinct().ToList();
        foreach (var m in mods) SendKey(m, true);
        SendKey(key, true);
        SendKey(key, false);
        for (int i = mods.Count - 1; i >= 0; i--) SendKey(mods[i], false);
    }

    private static void Send(NativeMethods.INPUT input)
    {
        lock (Gate)
        {
            NativeMethods.SendInput(1, new[] { input }, Marshal.SizeOf<NativeMethods.INPUT>());
        }
    }

    public static void SendAll(params NativeMethods.INPUT[] inputs)
    {
        lock (Gate)
        {
            NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
        }
    }
}
