using System.Text;
using System.Text.RegularExpressions;
using MouseMackros.Input;
using MouseMackros.Model;
using MouseMackros.Interop;
using MouseMackros.Services;

namespace MouseMackros.Engine;

public sealed class ActionExecutor : IDisposable
{
    private readonly Dictionary<string, CancellationTokenSource> _running = new();
    private readonly object _gate = new();
    private readonly Func<WindowInfo?>? _windowProvider;

    public ActionExecutor(Func<WindowInfo?>? windowProvider = null) =>
        _windowProvider = windowProvider;

    public event Action<string>? ProfileSwitchRequested;
    public event Action<int>? ProfileCycleRequested;
    public event Action<int>? LayerCycleRequested;
    public event Action<int>? LayerSetRequested;
    public event Action<string>? Error;

    public void ExecuteStart(string bindingKey, ActionSpec action)
    {
        // The hook callback must return fast, so everything happens off-thread.
        Task.Run(async () =>
        {
            try
            {
                switch (action.Kind)
                {
                    case ActionKind.PassThrough:
                    case ActionKind.Block:
                        return;

                    case ActionKind.Macro:
                        await SettleTriggerAsync(CancellationToken.None);
                        if (action.Mode == MacroMode.Once)
                        {
                            await RunMacroAsync(action, CancellationToken.None);
                        }
                        else if (action.Mode == MacroMode.Hold)
                        {
                            StartLoop(bindingKey, action);
                        }
                        else
                        {
                            ToggleLoop(bindingKey, action);
                        }
                        return;

                    default:
                        await SettleTriggerAsync(CancellationToken.None);
                        await RunSimpleAsync(action);
                        return;
                }
            }
            catch (Exception ex)
            {
                Error?.Invoke(ex.Message);
            }
        });
    }

    public void ExecuteEnd(string bindingKey, ActionSpec action)
    {
        if (action.Kind == ActionKind.Macro && action.Mode == MacroMode.Hold)
        {
            StopLoop(bindingKey);
        }
    }

    private void StartLoop(string key, ActionSpec action)
    {
        StopLoop(key);
        var cts = new CancellationTokenSource();
        lock (_gate) _running[key] = cts;
        Task.Run(() => LoopAsync(action, cts.Token));
    }

    private void ToggleLoop(string key, ActionSpec action)
    {
        lock (_gate)
        {
            if (_running.TryGetValue(key, out var existing))
            {
                existing.Cancel();
                _running.Remove(key);
                return;
            }
        }
        StartLoop(key, action);
    }

    private void StopLoop(string key)
    {
        lock (_gate)
        {
            if (_running.TryGetValue(key, out var cts))
            {
                cts.Cancel();
                _running.Remove(key);
            }
        }
    }

    private async Task LoopAsync(ActionSpec action, CancellationToken token)
    {
        int played = 0;
        while (!token.IsCancellationRequested)
        {
            try
            {
                await RunMacroAsync(action, token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            played++;
            if (action.Repeat > 0 && played >= action.Repeat) break;
            if (action.Repeat == 0)
            {
                // small gap so a held macro cannot spin the CPU
                try { await Task.Delay(10, token); } catch { break; }
            }
        }
    }

    /// <summary>
    /// The mouse sends its trigger chord as a series of HID reports, so Ctrl, Shift
    /// and Alt may still be physically held when the action starts. Waiting for the
    /// chord to finish (and releasing leftovers) keeps the injected action clean:
    /// otherwise Ctrl+Tab turns into Ctrl+Shift+Tab, or Ctrl+Shift+Tab does nothing.
    /// </summary>
    private static async Task SettleTriggerAsync(CancellationToken token)
    {
        string held = InputSender.DescribeHeldModifiers();
        var deadline = Environment.TickCount64 + 300;

        while (InputSender.AnyModifierDown() && Environment.TickCount64 < deadline)
        {
            try { await Task.Delay(5, token); }
            catch (OperationCanceledException) { break; }
        }

        InputSender.ReleaseHeldModifiers();

        if (held.Length > 0)
            ProfileStore.Log($"[macro] зажатые модификаторы до действия: {held}");

        // Let the synthetic releases reach the target window before the action.
        try { await Task.Delay(10, token); }
        catch (OperationCanceledException) { }
    }

    private async Task RunMacroAsync(ActionSpec action, CancellationToken token)
    {
        var script = action.Script;
        if (!string.IsNullOrWhiteSpace(action.ScriptPath))
        {
            if (File.Exists(action.ScriptPath))
            {
                try
                {
                    script = File.ReadAllText(action.ScriptPath);
                }
                catch (Exception ex)
                {
                    ReportError("Не удалось прочитать макрос: " + ex.Message);
                }
            }
            else
            {
                ReportError("Файл макроса не найден: " + action.ScriptPath);
            }
        }

        var steps = MacroScript.Parse(script);
        ProfileStore.Log($"[macro] {steps.Count} шагов: {script?.Replace("\n", " | ")}");
        if (steps.Count == 0) return;
        InputSender.ReleaseHeldModifiers();

        int repeats = Math.Max(0, action.Repeat);
        if (action.Mode == MacroMode.Once) repeats++;
        else repeats = Math.Max(1, repeats);

        for (int r = 0; r < repeats && !token.IsCancellationRequested; r++)
            await RunStepsAsync(steps, token, 0);
    }

    private async Task RunStepsAsync(List<MacroStep> steps, CancellationToken token, int depth)
    {
        if (depth > 12) return;

        foreach (var step in steps)
        {
            token.ThrowIfCancellationRequested();

            switch (step.Kind)
            {
                case "repeat":
                    for (int i = 0; i < step.Number; i++)
                        await RunStepsAsync(step.Body ?? new List<MacroStep>(), token, depth + 1);
                    break;

                case "if":
                {
                    bool result = false;
                    var window = _windowProvider?.Invoke();
                    if (!string.IsNullOrWhiteSpace(step.Text) && window is not null)
                    {
                        if (ExpressionEvaluator.TryEvaluate(step.Text, window, out var ok, out var error))
                            result = ok;
                        else
                            ReportError("Условие: " + error);
                    }

                    await RunStepsAsync(result
                        ? step.Body ?? new List<MacroStep>()
                        : step.ElseBody ?? new List<MacroStep>(), token, depth + 1);
                    break;
                }

                case "unknown":
                    ReportError("Неизвестный блок: " + (step.Text ?? ""));
                    break;

                default:
                    await RunStepAsync(step, token);
                    if (step.Kind != "delay")
                        await Task.Delay(20, token);
                    break;
            }
        }
    }

    private async Task RunStepAsync(MacroStep step, CancellationToken token)
    {
        switch (step.Kind)
        {
            case "delay":
                await Task.Delay(step.Number, token);
                break;

            case "text":
                await TypeTextAsync(ExpandText(step.Text ?? ""), token);
                break;

            case "wheel":
                InputSender.Wheel(step.Number * (step.Key == "down" ? -1 : 1),
                    step.Key is "left" or "right");
                break;

            case "move":
                InputSender.MoveRelative(step.Modifiers1, step.Modifiers2);
                break;

            case "browser":
            {
                var window = _windowProvider?.Invoke();
                string error = "";
                bool ok = step.Key switch
                {
                    "page" => BrowserMacro.TryChangePage(window, step.Number, out error),
                    "page-set" => BrowserMacro.TrySetPage(window, step.Number, out error),
                    "goto" => BrowserMacro.TryGoTo(window, ExpandText(step.Text ?? ""), out error),
                    "set_param" => BrowserMacro.TrySetQuery(window,
                        ExpandText(step.Text ?? ""), ExpandText(step.Text2 ?? ""), out error),
                    "change_param" => BrowserMacro.TryChangeQuery(window,
                        step.Text ?? "", step.Number, out error),
                    "set_param_url" => BrowserMacro.TrySetPath(window,
                        step.Number, ExpandText(step.Text ?? ""), out error),
                    "change_param_url" => BrowserMacro.TryChangePath(window,
                        step.Number, step.Modifiers1, out error),
                    "tab-new" => BrowserMacro.TryNewTab(window,
                        step.Text, step.Number, step.Modifiers1 != 0, out error),
                    "tab-close" => BrowserMacro.TryCloseTab(window,
                        step.Number, out error),
                    "tab-select" => BrowserMacro.TrySelectTab(window,
                        step.Number, out error),
                    "open-new-tab" => BrowserMacro.TryOpenInNewTab(window,
                        step.Text, out error),
                    "back" => BrowserMacro.TryGoBack(window, out error),
                    "forward" => BrowserMacro.TryGoForward(window, out error),
                    "stop" => BrowserMacro.TryStop(window, out error),
                    "reload-hard" => BrowserMacro.TryReloadHard(window, out error),
                    "tab-next" => BrowserMacro.TryNextTab(window, out error),
                    "tab-prev" => BrowserMacro.TryPrevTab(window, out error),
                    "tab-reopen" => BrowserMacro.TryReopenTab(window, out error),
                    "zoom-in" => BrowserMacro.TryZoom(window, 1, out error),
                    "zoom-out" => BrowserMacro.TryZoom(window, -1, out error),
                    "zoom-reset" => BrowserMacro.TryZoom(window, 0, out error),
                    "find" => BrowserMacro.TryFind(window,
                        ExpandText(step.Text ?? ""), out error),
                    "search" => BrowserMacro.TrySearch(window,
                        ExpandText(step.Text ?? ""), out error),
                    "copy-url" => BrowserMacro.TryCopyUrl(window, out error),
                    "scroll-top" => BrowserMacro.TryScroll(window, "top", out error),
                    "scroll-bottom" => BrowserMacro.TryScroll(window, "bottom", out error),
                    "update" => BrowserMacro.TryReload(window, out error),
                    _ => false
                };
                if (!ok)
                {
                    ReportError(string.IsNullOrWhiteSpace(error)
                        ? "Неизвестная или неподдерживаемая команда browser."
                        : error);
                }
                break;
            }

            case "clipboard-set":
                ReportIfFailed(SystemMacro.TrySetClipboard(
                    ExpandText(step.Text ?? ""), out var clipError), clipError);
                break;

            case "clipboard-paste":
                ReportIfFailed(SystemMacro.TryPasteClipboard(out var pasteError), pasteError);
                break;

            case "run":
                ReportIfFailed(SystemMacro.TryRun(
                    ExpandText(step.Text ?? ""), ExpandText(step.Text2 ?? ""), out var runError), runError);
                break;

            case "open":
                ReportIfFailed(SystemMacro.TryOpen(ExpandText(step.Text ?? ""), out var openError), openError);
                break;

            case "window":
            {
                string error;
                bool ok = step.Key == "activate"
                    ? SystemMacro.TryActivateWindow(ExpandText(step.Text ?? ""), out error)
                    : SystemMacro.TryWindowState(step.Key ?? "", out error);
                ReportIfFailed(ok, error);
                break;
            }

            case "volume":
                ReportIfFailed(SystemMacro.TryVolume(step.Number, out var volumeError), volumeError);
                break;

            case "mute":
                ReportIfFailed(SystemMacro.TryMute(out var muteError), muteError);
                break;

            case "media":
                ReportIfFailed(SystemMacro.TryMedia(step.Text ?? "", out var mediaError), mediaError);
                break;

            case "screenshot":
                ReportIfFailed(SystemMacro.TryScreenshot(out var shotError), shotError);
                break;

            case "lock":
                ReportIfFailed(SystemMacro.TryLock(out var lockError), lockError);
                break;

            case "sleep":
                ReportIfFailed(SystemMacro.TrySleep(out var sleepError), sleepError);
                break;

            case "profile-next":
                ProfileCycleRequested?.Invoke(1);
                break;

            case "profile-prev":
                ProfileCycleRequested?.Invoke(-1);
                break;

            case "profile-set":
                if (!string.IsNullOrWhiteSpace(step.Text))
                    ProfileSwitchRequested?.Invoke(step.Text);
                break;

            case "layer-next":
                LayerCycleRequested?.Invoke(1);
                break;

            case "layer-prev":
                LayerCycleRequested?.Invoke(-1);
                break;

            case "layer-set":
                LayerSetRequested?.Invoke(step.Number);
                break;

            case "press":
                if (step.Key is null) break;
                SendCombo(step.Key, step.Modifiers1, step.Modifiers2, true);
                break;

            case "down":
                if (step.Key is null) break;
                SendCombo(step.Key, step.Modifiers1, step.Modifiers2, false);
                break;

            case "up":
                if (step.Key is null) break;
                SendCombo(step.Key, step.Modifiers1, step.Modifiers2, false, true);
                break;

            case "pressmouse":
                InputSender.MouseButton(step.Key!, true);
                InputSender.MouseButton(step.Key!, false);
                break;

            case "downmouse":
                InputSender.MouseButton(step.Key!, true);
                break;

            case "upmouse":
                InputSender.MouseButton(step.Key!, false);
                break;
        }
    }

    private string ExpandText(string text)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains('{')) return text;

        var window = _windowProvider?.Invoke();
        var url = window?.Url ?? "";
        if (string.IsNullOrWhiteSpace(url) && window is not null &&
            BrowserUrlReader.IsBrowser(window.ProcessName))
        {
            url = BrowserUrlReader.TryGetUrl(window) ?? "";
        }

        return Regex.Replace(text, @"\{([^{}]+)\}", match =>
        {
            var key = match.Groups[1].Value.Trim();
            if (key.Equals("clipboard", StringComparison.OrdinalIgnoreCase))
                return SystemMacro.GetClipboardText();
            if (key.Equals("date", StringComparison.OrdinalIgnoreCase))
                return DateTime.Now.ToString("yyyy-MM-dd");
            if (key.Equals("time", StringComparison.OrdinalIgnoreCase))
                return DateTime.Now.ToString("HH:mm:ss");
            if (key.Equals("url", StringComparison.OrdinalIgnoreCase))
                return url;
            if (key.Equals("title", StringComparison.OrdinalIgnoreCase))
                return window?.Title ?? "";
            if (key.Equals("process", StringComparison.OrdinalIgnoreCase))
                return window?.ProcessName ?? "";
            if (key.StartsWith("param:", StringComparison.OrdinalIgnoreCase) &&
                SystemMacro.TryGetQueryParam(url, key[6..], out var value))
            {
                return value;
            }
            return match.Value;
        });
    }

    private void ReportIfFailed(bool ok, string error)
    {
        if (!ok)
            ReportError(string.IsNullOrWhiteSpace(error) ? "Команда не выполнена." : error);
    }

    private void ReportError(string message)
    {
        Error?.Invoke(message);
        ProfileStore.Log("[macro] " + message);
    }

    private static void SendCombo(string keyText, int mod1, int mod2, bool withUp, bool onlyUp = false)
    {
        if (!KeyNames.TryGetVk(keyText, out var vk) && !int.TryParse(keyText, out vk)) return;

        var mods = new List<int>();
        if (mod1 != 0) mods.Add(mod1);
        if (mod2 != 0) mods.Add(mod2);

        if (onlyUp)
        {
            InputSender.KeyUp(vk);
            for (int i = mods.Count - 1; i >= 0; i--) InputSender.KeyUp(mods[i]);
            return;
        }

        foreach (var m in mods) InputSender.KeyDown(m);
        InputSender.KeyDown(vk);
        if (withUp)
        {
            InputSender.KeyUp(vk);
            for (int i = mods.Count - 1; i >= 0; i--) InputSender.KeyUp(mods[i]);
        }
    }

    private static async Task TypeTextAsync(string text, CancellationToken token)
    {
        foreach (var ch in text)
        {
            token.ThrowIfCancellationRequested();
            InputSender.SendUnicode(ch, true);
            InputSender.SendUnicode(ch, false);
            await Task.Delay(3, token);
        }
    }

    private async Task RunSimpleAsync(ActionSpec action)
    {
        switch (action.Kind)
        {
            case ActionKind.Keys:
            {
                if (!KeyNames.TryParseCombo(action.Value, out var mods, out var key)) return;
                InputSender.ReleaseHeldModifiers();
                await Task.Delay(5);
                InputSender.Combo(mods, key);
                break;
            }
            case ActionKind.Text:
            {
                InputSender.ReleaseHeldModifiers();
                await Task.Delay(5);
                await TypeTextAsync(action.Value, CancellationToken.None);
                break;
            }
            case ActionKind.Media:
            {
                var vk = action.Value switch
                {
                    "PlayPause" => 0xB3,
                    "Next" => 0xB0,
                    "Previous" or "Prev" => 0xB1,
                    "Stop" => 0xB2,
                    "VolumeUp" => 0xAF,
                    "VolumeDown" => 0xAE,
                    "VolumeMute" or "Mute" => 0xAD,
                    _ => 0
                };
                if (vk == 0) return;
                InputSender.ReleaseHeldModifiers();
                InputSender.Combo(Array.Empty<int>(), vk);
                break;
            }
            case ActionKind.Scroll:
            {
                int ticks = 1;
                int.TryParse(action.Value, out ticks);
                if (ticks == 0) ticks = 1;
                InputSender.Wheel(ticks);
                break;
            }
            case ActionKind.Run:
            {
                if (string.IsNullOrWhiteSpace(action.Value)) return;
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = action.Value,
                    Arguments = action.Arguments ?? "",
                    UseShellExecute = true
                };
                System.Diagnostics.Process.Start(psi);
                break;
            }
            case ActionKind.Profile:
            {
                if (!string.IsNullOrWhiteSpace(action.Value))
                    ProfileSwitchRequested?.Invoke(action.Value);
                break;
            }
        }
    }

    public void StopAll()
    {
        lock (_gate)
        {
            foreach (var cts in _running.Values) cts.Cancel();
            _running.Clear();
        }
    }

    public void Dispose() => StopAll();
}
