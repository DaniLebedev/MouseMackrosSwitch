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
    private DispatcherTimer? _timer;
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

        // Poll on the UI thread: WinEvent callbacks and background threads can be
        // attached to a desktop where GetForegroundWindow returns 0.
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _timer.Tick += (_, _) => Poll();
        _timer.Start();
        Poll();
    }

    private void Poll()
    {
        try
        {
            var hwnd = NativeMethods.GetForegroundWindow();
            var info = Describe(hwnd);
            if (info is null || string.Equals(info.ProcessName, "unknown", StringComparison.OrdinalIgnoreCase))
                info = GetPointed() ?? info;
            if (info is null) return;

            // The title is part of the key: switching tabs inside one browser
            // window keeps the handle, but must re-evaluate title-based sub-profiles.
            var key = KeyOf(info);
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

        lock (_gate) _lastKey = KeyOf(info);
        ForegroundChanged?.Invoke(this, info);
    }

    private static string KeyOf(WindowInfo info) =>
        info.Handle + "|" + info.ProcessName + "|" + info.Title;

    public WindowInfo? GetForeground() => Describe(NativeMethods.GetForegroundWindow());

    /// <summary>Window under the cursor; a reliable fallback for mouse-button actions.</summary>
    public WindowInfo? GetPointed()
    {
        if (!NativeMethods.GetCursorPos(out var point)) return null;
        var hwnd = NativeMethods.GetAncestor(NativeMethods.WindowFromPoint(point), NativeMethods.GA_ROOT);
        return Describe(hwnd);
    }

    public static WindowInfo? Describe(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return null;
        try
        {
            NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);

            var path = pid == 0 ? null : NativeMethods.GetProcessPath(pid);
            string processName;
            if (!string.IsNullOrEmpty(path))
            {
                processName = Path.GetFileNameWithoutExtension(path);
            }
            else if (pid != 0)
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
            else
            {
                processName = "unknown";
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
        _timer?.Stop();
        _timer = null;
        if (_hook != IntPtr.Zero)
        {
            NativeMethods.UnhookWinEvent(_hook);
            _hook = IntPtr.Zero;
        }
        _started = false;
    }
}
