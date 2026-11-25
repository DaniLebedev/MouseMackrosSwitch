using System.Text;
using System.Text.RegularExpressions;
using MouseMackros.Interop;

namespace MouseMackros.Engine;

public sealed record MacroStep(
    string Kind,
    string? Key,
    int Modifiers1,
    int Modifiers2,
    int Number,
    string? Text,
    string? Text2 = null,
    List<MacroStep>? Body = null,
    List<MacroStep>? ElseBody = null);

/// <summary>
/// Tiny macro language, one command per line. Blocks use braces:
///   repeat 4 { ... } / if browser.url contains "nexus" { ... } else { ... }
/// </summary>
public static class MacroScript
{
    public static List<MacroStep> Parse(string? script)
    {
        if (string.IsNullOrWhiteSpace(script)) return new List<MacroStep>();

        var lines = script.Replace("\r\n", "\n").Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .ToList();

        int index = 0;
        return ParseBlock(lines, ref index, 0);
    }

    private static List<MacroStep> ParseBlock(List<string> lines, ref int index, int depth)
    {
        var steps = new List<MacroStep>();
        if (depth > 12) return steps;

        while (index < lines.Count)
        {
            var line = lines[index];

            if (line == "}")
            {
                index++;
                return steps;
            }

            if (IsElseLine(line)) return steps;

            if (line.EndsWith('{'))
            {
                var header = line[..^1].Trim();
                index++;

                if (header.StartsWith("repeat ", StringComparison.OrdinalIgnoreCase) ||
                    header.Equals("repeat", StringComparison.OrdinalIgnoreCase))
                {
                    int count = Math.Clamp(ParseInt(header.Length > 6 ? header[6..] : "", 1), 1, 1000);
                    var body = ParseBlock(lines, ref index, depth + 1);
                    steps.Add(new MacroStep("repeat", null, 0, 0, count, null, null, body));
                    continue;
                }

                if (header.StartsWith("if ", StringComparison.OrdinalIgnoreCase))
                {
                    var condition = header[3..].Trim();
                    var body = ParseBlock(lines, ref index, depth + 1);
                    List<MacroStep>? elseBody = null;

                    if (index < lines.Count && IsElseLine(lines[index]))
                    {
                        index++;
                        elseBody = ParseBlock(lines, ref index, depth + 1);
                    }

                    steps.Add(new MacroStep("if", null, 0, 0, 0, condition, null, body, elseBody));
                    continue;
                }

                // Unknown block: keep its body so the editor does not lose code.
                var unknownBody = ParseBlock(lines, ref index, depth + 1);
                steps.Add(new MacroStep("unknown", null, 0, 0, 0, header, null, unknownBody));
                continue;
            }

            var step = ParseLine(line);
            if (step is not null) steps.Add(step);
            index++;
        }

        return steps;
    }

    private static bool IsElseLine(string line)
    {
        var compact = line.Replace(" ", "").ToLowerInvariant();
        return compact == "}else{";
    }

    private static MacroStep? ParseLine(string line)
    {
        if (line.Length == 0 || line.StartsWith('#')) return null;

        var parts = line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var command = parts[0].ToLowerInvariant();
        var argument = parts.Length > 1 ? parts[1].Trim() : "";

        switch (command)
        {
            case "delay":
            case "wait":
                return new MacroStep("delay", null, 0, 0,
                    Math.Clamp(ParseInt(argument, 50), 0, 60_000), null);

            case "text":
            case "type":
                return new MacroStep("text", null, 0, 0, 0, Unquote(argument));

            case "wheel":
            {
                var bits = argument.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var dir = bits.Length > 0 ? bits[0].ToLowerInvariant() : "up";
                int count = bits.Length > 1 ? Math.Clamp(ParseInt(bits[1], 1), 1, 50) : 1;
                return new MacroStep("wheel", dir, 0, 0, count, null);
            }

            case "move":
            {
                var bits = argument.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                int dx = bits.Length > 0 ? ParseSigned(bits[0], 0) : 0;
                int dy = bits.Length > 1 ? ParseSigned(bits[1], 0) : 0;
                return new MacroStep("move", null, Math.Clamp(dx, -4000, 4000),
                    Math.Clamp(dy, -4000, 4000), 0, null);
            }

            case "cursor":
            {
                var bits = argument.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var dir = bits.Length > 0 ? bits[0].ToLowerInvariant() : "right";
                int amount = bits.Length > 1 ? Math.Clamp(ParseInt(bits[1], 10), 1, 127) : 10;
                int dx = dir is "left" or "влево" ? -amount : dir is "right" or "вправо" ? amount : 0;
                int dy = dir is "up" or "вверх" ? -amount : dir is "down" or "вниз" ? amount : 0;
                return new MacroStep("move", null, dx, dy, 0, null);
            }

            case "browser":
                return ParseBrowser(SplitArgs(argument));

            case "clipboard":
            {
                var args = SplitArgs(argument);
                var op = args.Count > 0 ? args[0].ToLowerInvariant() : "";
                return op switch
                {
                    "set" => new MacroStep("clipboard-set", null, 0, 0, 0, Rest(args, 1)),
                    "clear" => new MacroStep("clipboard-set", null, 0, 0, 0, ""),
                    "paste" => new MacroStep("clipboard-paste", null, 0, 0, 0, null),
                    _ => null
                };
            }

            case "run":
            {
                var args = SplitArgs(argument);
                if (args.Count == 0) return null;
                return new MacroStep("run", null, 0, 0, 0, args[0], JoinArgs(args, 1));
            }

            case "open":
                return new MacroStep("open", null, 0, 0, 0, Unquote(argument));

            case "window":
            {
                var args = SplitArgs(argument);
                var op = args.Count > 0 ? args[0].ToLowerInvariant() : "";
                return op switch
                {
                    "activate" when args.Count > 1 =>
                        new MacroStep("window", "activate", 0, 0, 0, Rest(args, 1)),
                    "minimize" or "min" => new MacroStep("window", "minimize", 0, 0, 0, null),
                    "maximize" or "max" => new MacroStep("window", "maximize", 0, 0, 0, null),
                    "restore" => new MacroStep("window", "restore", 0, 0, 0, null),
                    "close" => new MacroStep("window", "close", 0, 0, 0, null),
                    _ => null
                };
            }

            case "volume":
                return new MacroStep("volume", null, 0, 0,
                    Math.Clamp(ParseSigned(argument.Trim(), 1), -100, 100), null);

            case "mute":
                return new MacroStep("mute", null, 0, 0, 0, null);

            case "media":
            {
                var action = argument.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault()?.ToLowerInvariant() ?? "";
                return action.Length == 0 ? null : new MacroStep("media", null, 0, 0, 0, action);
            }

            case "screenshot":
                return new MacroStep("screenshot", null, 0, 0, 0, null);

            case "lock":
                return new MacroStep("lock", null, 0, 0, 0, null);

            case "sleep":
                return new MacroStep("sleep", null, 0, 0, 0, null);

            case "profile":
            {
                var args = SplitArgs(argument);
                var op = args.Count > 0 ? args[0].ToLowerInvariant() : "";
                if (op is "next" or "следующий") return new MacroStep("profile-next", null, 0, 0, 0, null);
                if (op is "prev" or "previous" or "предыдущий") return new MacroStep("profile-prev", null, 0, 0, 0, null);
                if (op == "set" && args.Count > 1) return new MacroStep("profile-set", null, 0, 0, 0, args[1]);
                if (args.Count == 1) return new MacroStep("profile-set", null, 0, 0, 0, args[0]);
                return null;
            }

            case "layer":
            {
                var args = SplitArgs(argument);
                var op = args.Count > 0 ? args[0].ToLowerInvariant() : "";
                if (op is "next" or "следующий") return new MacroStep("layer-next", null, 0, 0, 0, null);
                if (op is "prev" or "previous" or "предыдущий") return new MacroStep("layer-prev", null, 0, 0, 0, null);
                if (op == "set" && args.Count > 1 && int.TryParse(args[1], out var layer))
                    return new MacroStep("layer-set", null, 0, 0, Math.Clamp(layer, 0, 6), null);
                if (args.Count == 1 && int.TryParse(args[0], out var directLayer))
                    return new MacroStep("layer-set", null, 0, 0, Math.Clamp(directLayer, 0, 6), null);
                return null;
            }

            case "press":
            case "down":
            case "up":
            {
                if (argument.Equals("click", StringComparison.OrdinalIgnoreCase) ||
                    argument.Equals("lmb", StringComparison.OrdinalIgnoreCase))
                {
                    return new MacroStep(command + "mouse", "left", 0, 0, 0, null);
                }
                if (argument.StartsWith("mouse:", StringComparison.OrdinalIgnoreCase))
                {
                    var button = argument[6..].Trim().ToLowerInvariant();
                    return new MacroStep(command + "mouse", button, 0, 0, 0, null);
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
                    return new MacroStep(command, bareModifier.ToString(), 0, 0, 0, null);

                if (!KeyNames.TryParseCombo(argument, out var mods, out var key)) return null;
                int m1 = 0, m2 = 0;
                if (mods.Count > 0) m1 = mods[0];
                if (mods.Count > 1) m2 = mods[1];
                return new MacroStep(command, key.ToString(), m1, m2, 0, null);
            }

            default:
                return null;
        }
    }

    private static MacroStep? ParseBrowser(List<string> args)
    {
        var op = args.Count > 0 ? args[0].ToLowerInvariant() : "";
        switch (op)
        {
            case "goto" when args.Count > 1 && args[1].Length > 0:
                return new MacroStep("browser", "goto", 0, 0, 0, args[1]);

            case "page" when args.Count > 1:
            {
                var arg = args[1].ToLowerInvariant();
                if (arg is "+1" or "next" or "вперёд" or "вперед")
                    return new MacroStep("browser", "page", 0, 0, 1, null);
                if (arg is "-1" or "prev" or "previous" or "назад")
                    return new MacroStep("browser", "page", 0, 0, -1, null);
                if (int.TryParse(arg, out var page))
                    return new MacroStep("browser", "page-set", 0, 0,
                        Math.Clamp(page, 0, 1_000_000), null);
                return null;
            }

            case "set_param" when args.Count >= 3:
                return new MacroStep("browser", "set_param", 0, 0, 0, args[1], args[2]);

            case "change_param" when args.Count >= 3:
            {
                int delta = ParseSigned(args[2], 0);
                return delta == 0 ? null : new MacroStep("browser", "change_param", 0, 0,
                    Math.Clamp(delta, -1_000_000, 1_000_000), args[1]);
            }

            case "set_param_url" when args.Count >= 3:
                return int.TryParse(args[1], out var setIndex)
                    ? new MacroStep("browser", "set_param_url", 0, 0,
                        Math.Clamp(setIndex, 1, 128), args[2])
                    : null;

            case "change_param_url" when args.Count >= 3:
            {
                if (!int.TryParse(args[1], out var changeIndex)) return null;
                int delta = ParseSigned(args[2], 0);
                return delta == 0 ? null : new MacroStep("browser", "change_param_url",
                    Math.Clamp(delta, -1_000_000, 1_000_000), 0,
                    Math.Clamp(changeIndex, 1, 128), null);
            }

            case "tab" when args.Count > 1:
            {
                var sub = args[1].ToLowerInvariant();
                if (sub == "new")
                {
                    string? tabUrl = null;
                    int position = 0;
                    bool background = false;
                    for (int i = 2; i < args.Count; i++)
                    {
                        var token = args[i];
                        if (token.Equals("background", StringComparison.OrdinalIgnoreCase) ||
                            token.Equals("bg", StringComparison.OrdinalIgnoreCase))
                        {
                            background = true;
                        }
                        else if (int.TryParse(token, out var pos))
                        {
                            position = Math.Clamp(pos, 0, 128);
                        }
                        else if (string.IsNullOrWhiteSpace(tabUrl))
                        {
                            tabUrl = token;
                        }
                    }
                    return new MacroStep("browser", "tab-new",
                        background ? 1 : 0, 0, position, tabUrl);
                }

                if (sub == "close")
                {
                    int position = 0;
                    if (args.Count > 2) int.TryParse(args[2], out position);
                    return new MacroStep("browser", "tab-close", 0, 0,
                        Math.Clamp(position, 0, 128), null);
                }

                if (sub is "next" or "right" or "следующая")
                    return new MacroStep("browser", "tab-next", 0, 0, 0, null);
                if (sub is "prev" or "previous" or "left" or "предыдущая")
                    return new MacroStep("browser", "tab-prev", 0, 0, 0, null);
                if (sub is "reopen" or "restore" or "вернуть")
                    return new MacroStep("browser", "tab-reopen", 0, 0, 0, null);
                return null;
            }

            case "select" when args.Count > 1:
                return int.TryParse(args[1], out var tabIndex)
                    ? new MacroStep("browser", "tab-select", 0, 0,
                        Math.Clamp(tabIndex, 1, 128), null)
                    : null;

            case "open_in_new_tab" or "open-new-tab":
                return new MacroStep("browser", "open-new-tab", 0, 0, 0,
                    args.Count > 1 ? args[1] : null);

            case "back" or "назад":
                return new MacroStep("browser", "back", 0, 0, 0, null);

            case "forward" or "вперёд" or "вперед":
                return new MacroStep("browser", "forward", 0, 0, 0, null);

            case "stop":
                return new MacroStep("browser", "stop", 0, 0, 0, null);

            case "reload":
                return new MacroStep("browser",
                    args.Count > 1 && args[1].Equals("hard", StringComparison.OrdinalIgnoreCase)
                        ? "reload-hard"
                        : "update", 0, 0, 0, null);

            case "update":
                return new MacroStep("browser", "update", 0, 0, 0, null);

            case "zoom":
            {
                var arg = args.Count > 1 ? args[1].ToLowerInvariant() : "+1";
                int direction = arg switch
                {
                    "-1" or "out" or "-" or "меньше" => -1,
                    "0" or "reset" or "сброс" => 0,
                    _ => 1
                };
                return new MacroStep("browser",
                    direction > 0 ? "zoom-in" : direction < 0 ? "zoom-out" : "zoom-reset",
                    0, 0, 0, null);
            }

            case "find" when args.Count > 1:
                return new MacroStep("browser", "find", 0, 0, 0, Rest(args, 1));

            case "search" when args.Count > 1:
                return new MacroStep("browser", "search", 0, 0, 0, Rest(args, 1));

            case "copy_url" or "copy-url":
                return new MacroStep("browser", "copy-url", 0, 0, 0, null);

            case "scroll" when args.Count > 1:
                return new MacroStep("browser",
                    args[1].Equals("bottom", StringComparison.OrdinalIgnoreCase) ||
                    args[1].Equals("вниз", StringComparison.OrdinalIgnoreCase)
                        ? "scroll-bottom"
                        : "scroll-top", 0, 0, 0, null);

            default:
                return null;
        }
    }

    private static string Rest(List<string> args, int start) =>
        start >= args.Count ? "" : string.Join(" ", args.Skip(start));

    private static string JoinArgs(List<string> args, int start)
    {
        if (start >= args.Count) return "";
        return string.Join(" ", args.Skip(start)
            .Select(arg => arg.Contains(' ') ? "\"" + arg + "\"" : arg));
    }

    private static string Unquote(string s)
    {
        if (s.Length >= 2 && ((s[0] == '"' && s[^1] == '"') || (s[0] == '\'' && s[^1] == '\'')))
            return s[1..^1];
        return s;
    }

    private static List<string> SplitArgs(string text)
    {
        var result = new List<string>();
        int i = 0;
        while (i < text.Length)
        {
            while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
            if (i >= text.Length) break;

            char quote = text[i] is '"' or '\'' ? text[i] : '\0';
            if (quote != '\0') i++;

            var sb = new StringBuilder();
            while (i < text.Length)
            {
                char c = text[i];
                if (quote != '\0' && c == quote) { i++; break; }
                if (quote == '\0' && char.IsWhiteSpace(c)) break;
                if (c == '\\' && i + 1 < text.Length &&
                    (text[i + 1] == '\\' || text[i + 1] is '"' or '\''))
                {
                    i++;
                    sb.Append(text[i++]);
                    continue;
                }
                sb.Append(c);
                i++;
            }

            result.Add(sb.ToString());
        }
        return result;
    }

    private static int ParseInt(string s, int fallback)
    {
        var m = Regex.Match(s, @"\d+");
        return m.Success && int.TryParse(m.Value, out var v) ? v : fallback;
    }

    private static int ParseSigned(string s, int fallback) =>
        int.TryParse(s, System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out var v)
            ? v
            : fallback;

    public static string ToHelpText() =>
        "КЛАВИШИ И МЫШЬ\n" +
        "press Ctrl+A — нажать сочетание\n" +
        "down Shift / up Shift — зажать / отпустить\n" +
        "delay 100 — пауза, мс\n" +
        "text Привет — напечатать текст\n" +
        "wheel down 2 — прокрутить колесо\n" +
        "move 10 -5 — сдвинуть курсор (x y)\n" +
        "cursor right 10 — сдвинуть курсор\n" +
        "press mouse:left — клик мышью\n\n" +
        "БРАУЗЕР\n" +
        "browser page +1 / -1 / 287 — страница в ?page=\n" +
        "browser set_param \"page\" \"287\" — параметр URL\n" +
        "browser change_param \"page\" +1 — числовой параметр\n" +
        "browser set_param_url 2 \"999\" — сегмент пути\n" +
        "browser change_param_url 2 +1 — числовой сегмент\n" +
        "browser goto \"https://…\" — открыть URL\n" +
        "browser back / forward / stop — навигация\n" +
        "browser reload / reload hard — обновить / жёстко\n" +
        "browser tab new \"url\" 2 [background] — новая вкладка\n" +
        "browser tab close 2 — закрыть вкладку\n" +
        "browser tab next / prev / reopen — вкладки\n" +
        "browser select 3 — перейти на вкладку\n" +
        "browser open_in_new_tab [\"url\"] — открыть в новой вкладке\n" +
        "browser zoom +1 / -1 / reset — масштаб\n" +
        "browser find \"текст\" — поиск на странице\n" +
        "browser search \"запрос\" — поиск в браузере\n" +
        "browser copy_url — скопировать адрес\n" +
        "browser scroll top / bottom — в начало / конец\n\n" +
        "СИСТЕМА\n" +
        "clipboard set \"текст\" / clipboard paste / clipboard clear\n" +
        "text {clipboard}, {date}, {time}, {url}, {param:page} — подстановки\n" +
        "run \"notepad.exe\" — запустить программу\n" +
        "open \"D:\\folder\" — открыть файл, папку или ссылку\n" +
        "window activate \"Заголовок\" — переключить окно\n" +
        "window minimize / maximize / restore / close\n" +
        "volume +5 / -5, mute, media next / prev / play\n" +
        "screenshot — ножницы Windows, lock — блокировка, sleep — сон\n" +
        "profile next / prev / set games — профиль\n" +
        "layer next / prev / set 2 — ручной слой\n\n" +
        "БЛОКИ\n" +
        "repeat 5 { ... } — повторить\n" +
        "if browser.url contains \"nexus\" { ... } else { ... } — условие\n\n" +
        "VS CODE\n" +
        "«Открыть в VS Code» сохраняет .macro и открывает его.\n" +
        "Подсветка и проверка синтаксиса: tools\\vscode-mouse-macros.";
}
