using System.Text;
using System.Text.RegularExpressions;
using MouseMackros.Input;
using MouseMackros.Model;
using MouseMackros.Interop;
using MouseMackros.Services;

namespace MouseMackros.Engine;

public sealed record MacroStep(string Kind, string? Key, int Modifiers1, int Modifiers2, int Number, string? Text);

/// <summary>
/// Tiny macro language, one command per line:
///   press Ctrl+A | down Shift | up Shift | delay 100 | text hello | wheel up 3 | # comment
/// </summary>
public static class MacroScript
{
    public static List<MacroStep> Parse(string? script)
    {
        var steps = new List<MacroStep>();
        if (string.IsNullOrWhiteSpace(script)) return steps;

        foreach (var rawLine in script.Replace("\r\n", "\n").Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;

            var parts = line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            var command = parts[0].ToLowerInvariant();
            var argument = parts.Length > 1 ? parts[1].Trim() : "";

            switch (command)
            {
                case "delay":
                case "wait":
                    steps.Add(new MacroStep("delay", null, 0, 0, Math.Clamp(ParseInt(argument, 50), 0, 60_000), null));
                    break;

                case "text":
                case "type":
                    steps.Add(new MacroStep("text", null, 0, 0, 0, Unquote(argument)));
                    break;

                case "wheel":
                {
                    var bits = argument.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    var dir = bits.Length > 0 ? bits[0].ToLowerInvariant() : "up";
                    int count = bits.Length > 1 ? Math.Clamp(ParseInt(bits[1], 1), 1, 50) : 1;
                    steps.Add(new MacroStep("wheel", dir, 0, 0, count, null));
                    break;
                }

                case "press":
                case "down":
                case "up":
                {
                    if (argument.Equals("click", StringComparison.OrdinalIgnoreCase) ||
                        argument.Equals("lmb", StringComparison.OrdinalIgnoreCase))
                    {
                        steps.Add(new MacroStep(command + "mouse", "left", 0, 0, 0, null));
                        break;
                    }
                    if (argument.StartsWith("mouse:", StringComparison.OrdinalIgnoreCase))
                    {
                        var button = argument[6..].Trim().ToLowerInvariant();
                        steps.Add(new MacroStep(command + "mouse", button, 0, 0, 0, null));
                        break;
                    }
                    // A bare modifier ("down Shift") is a valid command on its own.
                    var bareModifier = argument.ToLowerInvariant() switch
                    {
                        "ctrl" or "control" => NativeMethods.VK_CONTROL,
                        "shift" => NativeMethods.VK_SHIFT,
                        "alt" => NativeMethods.VK_MENU,
                        "win" or "super" or "meta" => NativeMethods.VK_LWIN,
                        _ => 0
                    };
                    if (bareModifier != 0)
                    {
                        steps.Add(new MacroStep(command, bareModifier.ToString(), 0, 0, 0, null));
                        break;
                    }
                    if (!KeyNames.TryParseCombo(argument, out var mods, out var key)) continue;
                    int m1 = 0, m2 = 0;
                    if (mods.Count > 0) m1 = mods[0];
                    if (mods.Count > 1) m2 = mods[1];
                    steps.Add(new MacroStep(command, key.ToString(), m1, m2, 0, null));
                    break;
                }
            }
        }

        return steps;
    }

    private static string Unquote(string s)
    {
        if (s.Length >= 2 && ((s[0] == '"' && s[^1] == '"') || (s[0] == '\'' && s[^1] == '\'')))
            return s[1..^1];
        return s;
    }

    private static int ParseInt(string s, int fallback)
    {
        var m = Regex.Match(s, @"\d+");
        return m.Success && int.TryParse(m.Value, out var v) ? v : fallback;
    }

    public static string ToHelpText() =>
        "press Ctrl+A — нажать сочетание\n" +
        "down Shift — зажать\n" +
        "up Shift — отпустить\n" +
        "delay 100 — пауза, мс\n" +
        "text Привет — напечатать текст\n" +
        "wheel down 2 — прокрутить колесо\n" +
        "press mouse:left — клик мышью";
}

public sealed class ActionExecutor : IDisposable
{
    private readonly Dictionary<string, CancellationTokenSource> _running = new();
    private readonly object _gate = new();

    public event Action<string>? ProfileSwitchRequested;
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
                        if (action.Mode == MacroMode.Once)
                        {
                            await Task.Delay(8);
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
                        await Task.Delay(8);
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

    private async Task RunMacroAsync(ActionSpec action, CancellationToken token)
    {
        var steps = MacroScript.Parse(action.Script);
        ProfileStore.Log($"[macro] {steps.Count} шагов: {action.Script?.Replace("\n", " | ")}");
        if (steps.Count == 0) return;
        InputSender.ReleaseHeldModifiers();

        int repeats = Math.Max(0, action.Repeat);
        if (action.Mode == MacroMode.Once) repeats++;
        else repeats = Math.Max(1, repeats);

        for (int r = 0; r < repeats && !token.IsCancellationRequested; r++)
        {
            foreach (var step in steps)
            {
                token.ThrowIfCancellationRequested();
                switch (step.Kind)
                {
                    case "delay":
                        await Task.Delay(step.Number, token);
                        break;
                    case "text":
                        await TypeTextAsync(step.Text ?? "", token);
                        break;
                    case "wheel":
                        InputSender.Wheel(step.Number * (step.Key == "down" ? -1 : 1),
                            step.Key is "left" or "right");
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

                // Give the target application time to process each step; vendor
                // macros are played with a short pause between key events.
                if (step.Kind != "delay")
                    await Task.Delay(20, token);
            }
        }
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
