namespace MouseMackros.Input;

public sealed class KeyInputEventArgs : EventArgs
{
    public int Vk { get; init; }
    public int ScanCode { get; init; }
    public bool IsDown { get; init; }
    public bool IsInjected { get; init; }
    /// <summary>Combination including all currently held modifiers.</summary>
    public required string Combo { get; init; }
    /// <summary>Held modifiers, normalized: 0 = Ctrl, 1 = Shift, 2 = Alt, 3 = Win.</summary>
    public required HashSet<int> Modifiers { get; init; }
    /// <summary>Set to true to block the event from reaching other applications.</summary>
    public bool Handled { get; set; }
}

public sealed class MouseInputEventArgs : EventArgs
{
    public Trigger Trigger { get; init; } = Trigger.None;
    public bool IsDown { get; init; }
    public bool IsInjected { get; init; }
    public string DisplayName { get; init; } = "";
    public bool Handled { get; set; }
}

/// <summary>Global low level keyboard hook.</summary>
public sealed class KeyboardHook : IDisposable
{
    private readonly Interop.NativeMethods.LowLevelProc _proc;
    private IntPtr _hook;
    private readonly HashSet<int> _heldModifiers = new();

    public event EventHandler<KeyInputEventArgs>? KeyEvent;

    public KeyboardHook()
    {
        _proc = Callback;
        var module = Interop.NativeMethods.GetModuleHandle(null);
        _hook = Interop.NativeMethods.SetWindowsHookEx(Interop.NativeMethods.WH_KEYBOARD_LL, _proc, module, 0);
        if (_hook == IntPtr.Zero)
        {
            throw new InvalidOperationException("Не удалось установить клавиатурный хук. Ошибка " +
                                               System.Runtime.InteropServices.Marshal.GetLastWin32Error());
        }
    }

    public static bool IsModifier(int vk) =>
        vk is Interop.NativeMethods.VK_CONTROL or Interop.NativeMethods.VK_SHIFT or Interop.NativeMethods.VK_MENU
            or Interop.NativeMethods.VK_LWIN or Interop.NativeMethods.VK_RWIN
            or Interop.NativeMethods.VK_LCONTROL or Interop.NativeMethods.VK_RCONTROL
            or Interop.NativeMethods.VK_LSHIFT or Interop.NativeMethods.VK_RSHIFT
            or Interop.NativeMethods.VK_LMENU or Interop.NativeMethods.VK_RMENU;

    /// <summary>Currently held modifier set, normalized to Ctrl/Shift/Alt/Win.</summary>
    public IReadOnlyCollection<int> HeldModifiers => _heldModifiers;

    private IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            try
            {
                var data = System.Runtime.InteropServices.Marshal.PtrToStructure<Interop.NativeMethods.KBDLLHOOKSTRUCT>(lParam);
                var message = (int)wParam;
                bool isDown = message is Interop.NativeMethods.WM_KEYDOWN or Interop.NativeMethods.WM_SYSKEYDOWN;
                bool isInjected = (data.flags & Interop.NativeMethods.LLKHF_INJECTED) != 0;
                int vk = (int)data.vkCode;

                if (isInjected && data.dwExtraInfo == Interop.InputSender.Magic)
                {
                    return Interop.NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
                }

                bool modifier = IsModifier(vk);
                int normalized = Interop.KeyNames.NormalizeModifier(vk);

                // Build the modifier set as it is *before* this event applies.
                var mods = new HashSet<int>(_heldModifiers);
                if (modifier && isDown) mods.Add(normalized);
                if (modifier && !isDown) mods.Remove(normalized);

                if (modifier)
                {
                    if (isDown) _heldModifiers.Add(normalized);
                    else _heldModifiers.Remove(normalized);
                }

                var combo = Interop.KeyNames.FormatNormalized(mods, vk);
                var args = new KeyInputEventArgs
                {
                    Vk = vk,
                    ScanCode = (int)data.scanCode,
                    IsDown = isDown,
                    IsInjected = isInjected,
                    Combo = combo,
                    Modifiers = mods
                };

                KeyEvent?.Invoke(this, args);
                if (args.Handled)
                {
                    return new IntPtr(1);
                }
            }
            catch
            {
                // never let an exception escape a hook callback
            }
        }

        return Interop.NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            Interop.NativeMethods.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }
}

/// <summary>Global low level mouse hook (side buttons and wheel).</summary>
public sealed class MouseHook : IDisposable
{
    private readonly Interop.NativeMethods.LowLevelProc _proc;
    private IntPtr _hook;

    public event EventHandler<MouseInputEventArgs>? MouseEvent;

    public MouseHook()
    {
        _proc = Callback;
        var module = Interop.NativeMethods.GetModuleHandle(null);
        _hook = Interop.NativeMethods.SetWindowsHookEx(Interop.NativeMethods.WH_MOUSE_LL, _proc, module, 0);
        if (_hook == IntPtr.Zero)
        {
            throw new InvalidOperationException("Не удалось установить хук мыши. Ошибка " +
                                               System.Runtime.InteropServices.Marshal.GetLastWin32Error());
        }
    }

    private IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            try
            {
                var data = System.Runtime.InteropServices.Marshal.PtrToStructure<Interop.NativeMethods.MSLLHOOKSTRUCT>(lParam);
                if (data.dwExtraInfo == Interop.InputSender.Magic)
                {
                    return Interop.NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
                }

                int message = (int)wParam;
                MouseInputEventArgs? args = null;
                bool isInjected = (data.flags & Interop.NativeMethods.LLMHF_INJECTED) != 0;

                switch (message)
                {
                    case Interop.NativeMethods.WM_XBUTTONDOWN:
                    case Interop.NativeMethods.WM_XBUTTONUP:
                    {
                        int which = (int)(data.mouseData >> 16);
                        var button = which == 1 ? "X1" : "X2";
                        args = new MouseInputEventArgs
                        {
                            Trigger = Trigger.Mouse(button),
                            IsDown = message == Interop.NativeMethods.WM_XBUTTONDOWN,
                            IsInjected = isInjected,
                            DisplayName = Trigger.Mouse(button).DisplayName
                        };
                        break;
                    }
                    case Interop.NativeMethods.WM_MOUSEWHEEL:
                    {
                        int delta = (short)(data.mouseData >> 16);
                        if (delta == 0) break;
                        var dir = delta > 0 ? "Up" : "Down";
                        args = new MouseInputEventArgs
                        {
                            Trigger = Trigger.Wheel(dir),
                            IsDown = true,
                            IsInjected = isInjected,
                            DisplayName = Trigger.Wheel(dir).DisplayName
                        };
                        break;
                    }
                    case Interop.NativeMethods.WM_MOUSEHWHEEL:
                    {
                        int delta = (short)(data.mouseData >> 16);
                        if (delta == 0) break;
                        var dir = delta > 0 ? "Right" : "Left";
                        args = new MouseInputEventArgs
                        {
                            Trigger = Trigger.Wheel(dir),
                            IsDown = true,
                            IsInjected = isInjected,
                            DisplayName = Trigger.Wheel(dir).DisplayName
                        };
                        break;
                    }
                }

                if (args is not null)
                {
                    MouseEvent?.Invoke(this, args);
                    if (args.Handled) return new IntPtr(1);
                }
            }
            catch
            {
                // ignore
            }
        }

        return Interop.NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            Interop.NativeMethods.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }
}
