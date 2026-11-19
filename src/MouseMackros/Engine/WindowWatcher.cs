using System.Diagnostics;
using System.Text;
using System.Windows.Threading;
using MouseMackros.Interop;
using MouseMackros.Services;

namespace MouseMackros.Engine;

public sealed record WindowInfo(IntPtr Handle, string ProcessName, string? ProcessPath, string Title, string ClassName);

/// <summary>Tracks the foreground window.</summary>
public sealed class WindowWatcher : IDisposable
{
    private readonly NativeMethods.WinEventProc _proc;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly object _gate = new();
    private IntPtr _hook;
    private bool _started;
    private System.Threading.Timer? _timer;
    private string _lastKey = "";

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

        // A background poll makes switching deterministic even if the shell does
        // not deliver WinEvent callbacks.
        _timer = new System.Threading.Timer(_ => Poll(), null, 0, 300);
    }

    private void Poll()
    {
        try
        {
            var hwnd = NativeMethods.GetForegroundWindow();
            var info = Describe(hwnd);
            if (info is null) return;

            var key = info.Handle + "|" + info.ProcessName;
            lock (_gate)
            {
                if (key == _lastKey) return;
                _lastKey = key;
            }

            ProfileStore.Log($"[foreground] {info.ProcessName} | {info.Title}");
            _dispatcher.BeginInvoke(new Action(() => ForegroundChanged?.Invoke(this, info)));
        }
        catch
        {
            // ignore transient failures while windows are closing
        }
    }

    private void OnWinEvent(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild,
        uint dwEventThread, uint dwmsEventTime)
    {
        if (eventType != NativeMethods.EVENT_SYSTEM_FOREGROUND) return;
        var info = Describe(hwnd);
        if (info is null) return;

        lock (_gate) _lastKey = info.Handle + "|" + info.ProcessName;
        ForegroundChanged?.Invoke(this, info);
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
        _timer?.Dispose();
        _timer = null;
        if (_hook != IntPtr.Zero)
        {
            NativeMethods.UnhookWinEvent(_hook);
            _hook = IntPtr.Zero;
        }
        _started = false;
    }
}
