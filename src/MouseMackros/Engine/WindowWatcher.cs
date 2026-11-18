using System.Diagnostics;
using System.Text;
using MouseMackros.Interop;

namespace MouseMackros.Engine;

public sealed record WindowInfo(IntPtr Handle, string ProcessName, string? ProcessPath, string Title, string ClassName);

/// <summary>Tracks the foreground window.</summary>
public sealed class WindowWatcher : IDisposable
{
    private readonly NativeMethods.WinEventProc _proc;
    private IntPtr _hook;
    private bool _started;

    public event EventHandler<WindowInfo>? ForegroundChanged;

    public WindowWatcher()
    {
        _proc = OnWinEvent;
    }

    public void Start()
    {
        if (_started) return;
        _started = true;
        _hook = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_SYSTEM_FOREGROUND, NativeMethods.EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero, _proc, 0, 0,
            NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS);

        var current = GetForeground();
        if (current is not null) ForegroundChanged?.Invoke(this, current);
    }

    private void OnWinEvent(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild,
        uint dwEventThread, uint dwmsEventTime)
    {
        if (eventType != NativeMethods.EVENT_SYSTEM_FOREGROUND) return;
        var info = Describe(hwnd);
        if (info is not null) ForegroundChanged?.Invoke(this, info);
    }

    public WindowInfo? GetForeground() => Describe(NativeMethods.GetForegroundWindow());

    public static WindowInfo? Describe(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return null;
        try
        {
            NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == 0) return null;

            var path = NativeMethods.GetProcessPath(pid);
            string processName;
            if (!string.IsNullOrEmpty(path))
            {
                processName = Path.GetFileNameWithoutExtension(path);
            }
            else
            {
                try
                {
                    processName = Process.GetProcessById((int)pid).ProcessName;
                }
                catch
                {
                    processName = "pid" + pid;
                }
            }

            var title = new StringBuilder(512);
            NativeMethods.GetWindowText(hwnd, title, title.Capacity);

            var cls = new StringBuilder(256);
            NativeMethods.GetClassName(hwnd, cls, cls.Capacity);

            return new WindowInfo(hwnd, processName.ToLowerInvariant(), path, title.ToString(), cls.ToString());
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            NativeMethods.UnhookWinEvent(_hook);
            _hook = IntPtr.Zero;
        }
        _started = false;
    }
}
