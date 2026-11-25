using System.IO;
using System.Text;
using System.Text.Json;
using MouseMackros.Engine;
using MouseMackros.Input;
using MouseMackros.Interop;
using MouseMackros.Model;
using MouseMackros.Services;

var failures = 0;
var passed = 0;

void Check(string name, bool condition, string? detail = null)
{
    if (condition)
    {
        passed++;
        Console.WriteLine("  OK   " + name);
    }
    else
    {
        failures++;
        Console.WriteLine("  FAIL " + name + (detail is null ? "" : "  →  " + detail));
    }
}

Console.WriteLine("MouseMackros self test");
Console.WriteLine();

// ---------------------------------------------------------------- presets
var config = Presets.CreateDefault();
Check("2 профиля по умолчанию", config.Profiles.Count == 2, config.Profiles.Count.ToString());
Check("резервный профиль = default", config.FallbackProfile.Id == "default", config.FallbackProfile.Id);
Check("браузер: 12 действий", config.Profiles[1].Bindings.Count == 12,
    config.Profiles[1].Bindings.Count.ToString());
Check("браузер: все действия на кнопках",
    config.Profiles[1].Bindings.All(b => b.Button is >= 1 and <= 16));
Check("браузер: триггеры - Ctrl+Alt+Shift",
    config.Profiles[1].Bindings.All(b => b.Trigger.StartsWith("key:Ctrl+Shift+Alt+")));

// ---------------------------------------------------------------- json round trip
var json = JsonSerializer.Serialize(config, ProfileStore.JsonOptions);
var back = JsonSerializer.Deserialize<AppConfig>(json, ProfileStore.JsonOptions)!;
Check("JSON: профили сохраняются", back.Profiles.Count == config.Profiles.Count);
Check("JSON: действия сохраняются",
    back.Profiles[1].Bindings.Count == config.Profiles[1].Bindings.Count);
Check("JSON: тип действия читается",
    back.Profiles[1].Bindings[0].Action.Kind == ActionKind.Keys,
    back.Profiles[1].Bindings[0].Action.Kind.ToString());
Check("JSON: триггер сохраняется",
    back.Profiles[1].Bindings[0].Trigger == config.Profiles[1].Bindings[0].Trigger,
    back.Profiles[1].Bindings[0].Trigger);
Check("JSON: номер кнопки сохраняется", back.Profiles[1].Bindings[0].Button == 1,
    back.Profiles[1].Bindings[0].Button.ToString());

// ---------------------------------------------------------------- wildcard matching
Check("wildcard: chrome == chrome", ProfileEngine.Wildcard("chrome", "chrome"));
Check("wildcard: Chrome регистронезависим", ProfileEngine.Wildcard("Chrome", "chrome"));
Check("wildcard: *game*", ProfileEngine.Wildcard("*game*", "mygameclient"));
Check("wildcard: game.exe", ProfileEngine.Wildcard("game.exe", "game"));
Check("wildcard: нет ложного совпадения", !ProfileEngine.Wildcard("chrome", "msedge"));
Check("mouse:Left → кнопка 12", MouseButtonMap.ButtonForTrigger("mouse:Left") == 12,
    MouseButtonMap.ButtonForTrigger("mouse:Left").ToString());
Check("mouse:Right → кнопка 13", MouseButtonMap.ButtonForTrigger("mouse:Right") == 13,
    MouseButtonMap.ButtonForTrigger("mouse:Right").ToString());
Check("mouse:Middle → кнопка 14", MouseButtonMap.ButtonForTrigger("mouse:Middle") == 14,
    MouseButtonMap.ButtonForTrigger("mouse:Middle").ToString());

// ---------------------------------------------------------------- sub-profiles
var nested = new AppConfig { DefaultProfileId = "fallback" };
var fallback = new Profile
{
    Id = "fallback", Name = "Везде", IsFallback = true,
    Match = new MatchRules { Priority = -100 }
};
var browser = new Profile
{
    Id = "browser", Name = "Браузер",
    Match = new MatchRules { Priority = 20, Processes = { "firefox", "chrome" } }
};
var youtube = new Profile
{
    Id = "youtube", Name = "Браузер · YouTube", ParentId = "browser",
    Match = new MatchRules { TitleContains = "YouTube", Priority = 0 }
};
nested.Profiles.AddRange(new[] { fallback, browser, youtube });

WindowInfo Win(string process, string title) => new(IntPtr.Zero, process, null, title, "");
WindowInfo WinUrl(string process, string title, string url) =>
    new(IntPtr.Zero, process, null, title, "", url);

Check("условие: browser.url contains",
    ExpressionEvaluator.TryEvaluate("browser.url contains \"youtube.com\"",
        WinUrl("firefox", "Video", "https://www.youtube.com/watch?v=1"), out var e1, out _) && e1);
Check("условие: process && title",
    ExpressionEvaluator.TryEvaluate("process == \"firefox\" && title contains \"YouTube\"",
        Win("firefox", "YouTube - Firefox"), out var e2, out _) && e2);
Check("условие: отрицание и ИЛИ",
    ExpressionEvaluator.TryEvaluate("!(process == \"chrome\") || title contains \"x\"",
        Win("firefox", "YouTube"), out var e3, out _) && e3);
Check("условие: == с шаблоном",
    ExpressionEvaluator.TryEvaluate("title == \"You*\"", Win("firefox", "YouTube"), out var e4, out _) && e4);
Check("условие: regex",
    ExpressionEvaluator.TryEvaluate("title ~= \"^You\"", Win("firefox", "YouTube"), out var e5, out _) && e5);
Check("условие: ошибка синтаксиса",
    !ExpressionEvaluator.TryEvaluate("browser.url = \"x\"", Win("firefox", "x"), out _, out _));
Check("условие: простое слово → title contains",
    ExpressionEvaluator.NormalizeCondition("YouTube") == "title contains \"YouTube\"",
    ExpressionEvaluator.NormalizeCondition("YouTube"));
Check("условие: домен → browser.url contains",
    ExpressionEvaluator.NormalizeCondition("youtube.com") == "browser.url contains \"youtube.com\"",
    ExpressionEvaluator.NormalizeCondition("youtube.com"));

Check("URL: Firefox без схемы",
    BrowserUrlReader.NormalizeUrl("www.nexusmods.com/mods?page=2") ==
    "https://www.nexusmods.com/mods?page=2",
    BrowserUrlReader.NormalizeUrl("www.nexusmods.com/mods?page=2") ?? "null");
Check("URL: localhost получает http",
    BrowserUrlReader.NormalizeUrl("localhost:3000/test") == "http://localhost:3000/test",
    BrowserUrlReader.NormalizeUrl("localhost:3000/test") ?? "null");
Check("URL: поисковый запрос не URL",
    BrowserUrlReader.NormalizeUrl("hello world") is null &&
    BrowserUrlReader.NormalizeUrl("nexusmods") is null);
Check("URL: схема сохраняется",
    BrowserUrlReader.NormalizeUrl("http://example.com/a") == "http://example.com/a");
var normalizedNexus = BrowserUrlReader.NormalizeUrl("www.nexusmods.com/mods?page=2") ?? "";
Check("URL: условие с https после Firefox",
    ExpressionEvaluator.TryEvaluate("browser.url contains \"https://www.nexusmods.com\"",
        WinUrl("firefox", "Mods", normalizedNexus), out var e6, out _) && e6,
    normalizedNexus);

var urlChild = new Profile
{
    Id = "yt-url", Name = "Браузер · YouTube URL", ParentId = "browser",
    Match = new MatchRules { Expression = "browser.url contains \"youtube.com\"" }
};
nested.Profiles.Add(urlChild);
Check("подпрофиль по URL",
    ProfileEngine.SelectProfile(nested, WinUrl("firefox", "Любой заголовок", "https://www.youtube.com/watch?v=1")).Id == "yt-url",
    ProfileEngine.SelectProfile(nested, WinUrl("firefox", "Любой заголовок", "https://www.youtube.com/watch?v=1")).Id);

Check("подпрофиль: глубина = 1", nested.DepthOf(youtube) == 1, nested.DepthOf(youtube).ToString());
Check("подпрофиль: YouTube выигрывает у родителя",
    ProfileEngine.SelectProfile(nested, Win("firefox", "Клип - YouTube")).Id == "youtube",
    ProfileEngine.SelectProfile(nested, Win("firefox", "Клип - YouTube")).Id);
Check("подпрофиль: обычная страница → родитель",
    ProfileEngine.SelectProfile(nested, Win("firefox", "Новая вкладка")).Id == "browser",
    ProfileEngine.SelectProfile(nested, Win("firefox", "Новая вкладка")).Id);
Check("подпрофиль: родитель не совпал → fallback",
    ProfileEngine.SelectProfile(nested, Win("notepad", "Клип - YouTube")).Id == "fallback",
    ProfileEngine.SelectProfile(nested, Win("notepad", "Клип - YouTube")).Id);
Check("подпрофиль: глубина важнее приоритета",
    ProfileEngine.SelectProfile(nested, Win("chrome", "YouTube")).Id == "youtube",
    ProfileEngine.SelectProfile(nested, Win("chrome", "YouTube")).Id);

var manualLayer = new Profile
{
    Id = "layer1", Name = "Слой 1", ParentId = "browser", IsManualLayer = true,
    LayerOrder = 1, Match = new MatchRules { TitleContains = "LayerOnly" }
};
nested.Profiles.Add(manualLayer);
Check("ручной слой не выбирается автоматически",
    ProfileEngine.SelectProfile(nested, Win("firefox", "LayerOnly")).Id == "browser",
    ProfileEngine.SelectProfile(nested, Win("firefox", "LayerOnly")).Id);
Check("ручные слои родителя находятся",
    nested.ManualLayersOf(browser).Count == 1 &&
    nested.ManualLayersOf(browser)[0].Id == "layer1");

var nestedJson = JsonSerializer.Serialize(nested, ProfileStore.JsonOptions);
var nestedBack = JsonSerializer.Deserialize<AppConfig>(nestedJson, ProfileStore.JsonOptions)!;
Check("JSON: ParentId сохраняется", nestedBack.GetProfile("youtube")?.ParentId == "browser",
    nestedBack.GetProfile("youtube")?.ParentId ?? "null");

var orphan = new AppConfig { Profiles = { new Profile { Id = "orphan", ParentId = "missing" } } };
ProfileStore.Normalize(orphan);
Check("подпрофиль: потерянный родитель сбрасывается", orphan.GetProfile("orphan")?.ParentId is null,
    orphan.GetProfile("orphan")?.ParentId ?? "null");

// ---------------------------------------------------------------- keys
Check("Ctrl+Alt+Shift+A разбирается",
    KeyNames.TryParseCombo("Ctrl+Alt+Shift+A", out var mods, out var key) && key == 0x41 && mods.Count == 3);
Check("формат сочетания",
    KeyNames.NormalizeCombo("Ctrl+Alt+Shift+A") == "Ctrl+Shift+Alt+A",
    KeyNames.NormalizeCombo("Ctrl+Alt+Shift+A"));
Check("формат из нормализованных модификаторов",
    KeyNames.FormatNormalized(new[] { 0, 2 }, 0x7B) == "Ctrl+Alt+F12",
    KeyNames.FormatNormalized(new[] { 0, 2 }, 0x7B));
Check("имя клавиши F10", KeyNames.GetName(0x79) == "F10", KeyNames.GetName(0x79));
Check("описание сочетания", KeyNames.DescribeCombo("Alt+Left").Contains("←"),
    KeyNames.DescribeCombo("Alt+Left"));

// ---------------------------------------------------------------- triggers
var keyTrigger = Trigger.Key("Ctrl+Alt+Shift+A");
Check("триггер key round trip", Trigger.Parse(keyTrigger.ToString()).ToString() == keyTrigger.ToString());
Check("триггер mouse round trip", Trigger.Parse("mouse:X1").ToString() == "mouse:X1");
Check("триггер wheel", Trigger.Parse("wheel:Down").DisplayName.Contains("вниз"),
    Trigger.Parse("wheel:Down").DisplayName);
Check("пустой триггер", Trigger.Parse("").IsEmpty);

// ---------------------------------------------------------------- button map
Check("16 кнопок в карте", MouseButtonMap.All.Count == 16, MouseButtonMap.All.Count.ToString());
Check("13 редактируемых кнопок", MouseButtonMap.Editable.Count() == 13,
    MouseButtonMap.Editable.Count().ToString());
Check("кнопка 1 → Key7", MouseButtonMap.Get(1)?.KeyIndex == 7);
Check("кнопка 7 → Key14", MouseButtonMap.Get(7)?.KeyIndex == 14);
Check("кнопка 9 → Key13", MouseButtonMap.Get(9)?.KeyIndex == 13);
Check("кнопка 12 = ЛКМ, не редактируется", MouseButtonMap.Get(12) is { Editable: false, KeyIndex: 0 });
Check("триггер кнопки 1", MouseButtonMap.TriggerFor(1) == "key:Ctrl+Shift+Alt+A",
    MouseButtonMap.TriggerFor(1));
Check("кнопка 1 по триггеру", MouseButtonMap.ButtonForTrigger(MouseButtonMap.TriggerFor(1)) == 1);
Check("кнопка 16 по триггеру", MouseButtonMap.ButtonForTrigger("key:Ctrl+Alt+Shift+M") == 16,
    MouseButtonMap.ButtonForTrigger("key:Ctrl+Alt+Shift+M").ToString());
Check("обычное сочетание не кнопка", MouseButtonMap.ButtonForTrigger("key:Ctrl+C") == 0);
Check("X1 не кнопка", MouseButtonMap.ButtonForTrigger("mouse:X1") == 0);
Check("кнопки 12-14 не назначаются", MouseButtonMap.TriggerFor(12) == "" && MouseButtonMap.TriggerFor(14) == "");

var echoConfig = Presets.CreateDefault();
var echoProfile = new Profile { Name = "Echo" };
foreach (var info in MouseButtonMap.Editable)
{
    echoProfile.Bindings.Add(new MouseMackros.Model.Binding
    {
        Button = info.Number,
        Trigger = MouseButtonMap.TriggerFor(info.Number),
        Action = new ActionSpec { Kind = ActionKind.Keys, Value = "Ctrl+Alt+Shift+" + info.Letter }
    });
}
echoConfig.Profiles.Add(echoProfile);
ProfileStore.Normalize(echoConfig);
Check("эхо-бинги MouseMackros.psf удалены",
    echoProfile.Bindings.Count == 0 && echoProfile.VendorProfilePath is null);

// ---------------------------------------------------------------- macro parser
var steps = MacroScript.Parse("""
    # comment
    press Ctrl+A
    delay 120
    text hello
    wheel down 2
    down Shift
    up Shift
    press mouse:left
    press nonsense
    """);
Check("макрос: 7 шагов", steps.Count == 7, steps.Count.ToString());
Check("макрос: delay = 120", steps[1].Kind == "delay" && steps[1].Number == 120);
Check("макрос: текст", steps[2].Kind == "text" && steps[2].Text == "hello");
Check("макрос: колесо", steps[3].Kind == "wheel" && steps[3].Number == 2);
Check("макрос: комбинация разобрана", steps[0].Kind == "press" && steps[0].Modifiers1 == 0x11);
Check("макрос: мусор игнорируется", steps.All(s => s.Kind != "press" || s.Key != "0"));

var tabMacro = MacroScript.Parse("down Ctrl\ndown Shift\ndown Tab\nup Tab\nup Shift\nup Ctrl");
Check("макрос вкладок: 6 шагов", tabMacro.Count == 6, tabMacro.Count.ToString());
Check("макрос вкладок: Ctrl зажат",
    tabMacro[0].Kind == "down" && tabMacro[0].Key == "17", tabMacro[0].Key);
Check("макрос вкладок: Tab отпущен",
    tabMacro[3].Kind == "up" && tabMacro[3].Key == "9", tabMacro[3].Key);

var motion = MacroScript.Parse("move 10 -5\ncursor right 7\ncursor up 3");
Check("макрос: move", motion.Count == 3 &&
    motion[0].Kind == "move" && motion[0].Modifiers1 == 10 && motion[0].Modifiers2 == -5);
Check("макрос: cursor right", motion[1].Modifiers1 == 7 && motion[1].Modifiers2 == 0);
Check("макрос: cursor up", motion[2].Modifiers1 == 0 && motion[2].Modifiers2 == -3);

Check("browser: page +1",
    BrowserMacro.TryReplacePage("https://www.nexusmods.com/games?sort=mods&page=286",
        1, out var pageNext, out _) && pageNext.EndsWith("page=287"),
    pageNext);
Check("browser: page -1",
    BrowserMacro.TryReplacePage("https://www.nexusmods.com/games?page=286", -1, out var pagePrev, out _) &&
    pagePrev.EndsWith("page=285"), pagePrev);
Check("browser: page set",
    BrowserMacro.TrySetPageValue("https://x.test/list?page=5", 287, out var pageSet, out _) &&
    pageSet.EndsWith("page=287"), pageSet);
Check("browser: page append",
    BrowserMacro.TrySetPageValue("https://x.test/list?sort=mods", 2, out var pageAdd, out _) &&
    pageAdd.Contains("&page=2"), pageAdd);
Check("browser: no page parameter",
    !BrowserMacro.TryReplacePage("https://x.test/list", 1, out _, out _));

var browserSteps = MacroScript.Parse("browser page +1\nbrowser goto \"https://x.test\"");
Check("макрос: browser page", browserSteps.Count == 2 &&
    browserSteps[0].Kind == "browser" && browserSteps[0].Key == "page" && browserSteps[0].Number == 1);
Check("макрос: browser goto", browserSteps[1].Key == "goto" &&
    browserSteps[1].Text == "https://x.test", browserSteps[1].Text ?? "null");

Check("browser: set query existing",
    BrowserMacro.TrySetQueryValue("https://x.test/list?page=5&sort=mods", "page", "287",
        out var q1, out _) && q1.Contains("page=287") && q1.Contains("sort=mods"), q1);
Check("browser: set query new",
    BrowserMacro.TrySetQueryValue("https://x.test/list?sort=mods", "page", "287",
        out var q2, out _) && q2.Contains("&page=287"), q2);
Check("browser: change query",
    BrowserMacro.TryChangeQueryValue("https://x.test/list?page=5", "page", 1,
        out var q3, out _) && q3.EndsWith("page=6"), q3);
Check("browser: set path",
    BrowserMacro.TrySetPathValue("https://steamcommunity.com/app/1366540", 2, "999",
        out var p1, out _) && p1 == "https://steamcommunity.com/app/999", p1);
Check("browser: change path",
    BrowserMacro.TryChangePathValue("https://steamcommunity.com/app/1366540", 2, 1,
        out var p2, out _) && p2 == "https://steamcommunity.com/app/1366541", p2);
Check("система: чтение параметра URL",
    SystemMacro.TryGetQueryParam("https://x.test/a?page=286&sort=mods", "page", out var paramValue) &&
    paramValue == "286", paramValue);

var extended = MacroScript.Parse("""
    browser set_param "page" "287"
    browser change_param "page" +1
    browser set_param_url 2 "999"
    browser change_param_url 2 -1
    browser tab new "https://x.test" 2
    browser tab close 2
    browser update
    """);
Check("макрос: set_param", extended[0].Key == "set_param" &&
    extended[0].Text == "page" && extended[0].Text2 == "287");
Check("макрос: change_param", extended[1].Key == "change_param" &&
    extended[1].Text == "page" && extended[1].Number == 1);
Check("макрос: set_param_url", extended[2].Key == "set_param_url" &&
    extended[2].Number == 2 && extended[2].Text == "999");
Check("макрос: change_param_url", extended[3].Key == "change_param_url" &&
    extended[3].Number == 2 && extended[3].Modifiers1 == -1);
Check("макрос: tab new", extended[4].Key == "tab-new" &&
    extended[4].Number == 2 && extended[4].Text == "https://x.test");
Check("макрос: tab close", extended[5].Key == "tab-close" && extended[5].Number == 2);
Check("макрос: update", extended[6].Key == "update");

var cheapBrowser = MacroScript.Parse("""
    browser back
    browser forward
    browser stop
    browser reload hard
    browser tab next
    browser tab prev
    browser tab reopen
    browser zoom +1
    browser zoom -1
    browser zoom reset
    browser find "hello"
    browser search "nexus mods"
    browser copy_url
    browser scroll top
    browser scroll bottom
    browser select 3
    browser open_in_new_tab "https://x.test"
    browser tab new "https://x.test" 2 background
    """);
Check("browser: back/forward", cheapBrowser[0].Key == "back" && cheapBrowser[1].Key == "forward");
Check("browser: stop", cheapBrowser[2].Key == "stop");
Check("browser: reload hard", cheapBrowser[3].Key == "reload-hard");
Check("browser: tab next/prev/reopen", cheapBrowser[4].Key == "tab-next" &&
    cheapBrowser[5].Key == "tab-prev" && cheapBrowser[6].Key == "tab-reopen");
Check("browser: zoom", cheapBrowser[7].Key == "zoom-in" &&
    cheapBrowser[8].Key == "zoom-out" && cheapBrowser[9].Key == "zoom-reset");
Check("browser: find/search", cheapBrowser[10].Key == "find" &&
    cheapBrowser[10].Text == "hello" && cheapBrowser[11].Key == "search" &&
    cheapBrowser[11].Text == "nexus mods");
Check("browser: copy/scroll", cheapBrowser[12].Key == "copy-url" &&
    cheapBrowser[13].Key == "scroll-top" && cheapBrowser[14].Key == "scroll-bottom");
Check("browser: select", cheapBrowser[15].Key == "tab-select" && cheapBrowser[15].Number == 3);
Check("browser: open in new tab", cheapBrowser[16].Key == "open-new-tab" &&
    cheapBrowser[16].Text == "https://x.test");
Check("browser: background tab", cheapBrowser[17].Key == "tab-new" &&
    cheapBrowser[17].Modifiers1 == 1 && cheapBrowser[17].Number == 2);

var systemMacro = MacroScript.Parse("""
    clipboard set "hello world"
    clipboard paste
    run "notepad.exe" "C:\Temp\a.txt"
    open "D:\Projects"
    window activate "Notepad"
    window minimize
    window maximize
    window restore
    window close
    volume +5
    volume -3
    mute
    media next
    screenshot
    lock
    sleep
    profile next
    profile prev
    profile set games
    layer next
    layer prev
    layer set 2
    text {clipboard} {date} {time} {url} {param:page}
    """);
Check("система: clipboard", systemMacro[0].Kind == "clipboard-set" &&
    systemMacro[0].Text == "hello world" && systemMacro[1].Kind == "clipboard-paste");
Check("система: run/open", systemMacro[2].Kind == "run" &&
    systemMacro[2].Text == "notepad.exe" && systemMacro[2].Text2 == "C:\\Temp\\a.txt" &&
    systemMacro[3].Kind == "open" && systemMacro[3].Text == "D:\\Projects",
    $"{systemMacro[2].Kind}|{systemMacro[2].Text}|{systemMacro[2].Text2}|{systemMacro[3].Kind}|{systemMacro[3].Text}");
Check("система: window", systemMacro[4].Key == "activate" &&
    systemMacro[4].Text == "Notepad" && systemMacro[5].Key == "minimize" &&
    systemMacro[6].Key == "maximize" && systemMacro[7].Key == "restore" &&
    systemMacro[8].Key == "close");
Check("система: volume/mute/media", systemMacro[9].Kind == "volume" &&
    systemMacro[9].Number == 5 && systemMacro[10].Number == -3 &&
    systemMacro[11].Kind == "mute" && systemMacro[12].Kind == "media");
Check("система: screenshot/lock/sleep", systemMacro[13].Kind == "screenshot" &&
    systemMacro[14].Kind == "lock" && systemMacro[15].Kind == "sleep");
Check("система: profile", systemMacro[16].Kind == "profile-next" &&
    systemMacro[17].Kind == "profile-prev" && systemMacro[18].Kind == "profile-set" &&
    systemMacro[18].Text == "games");
Check("система: layer", systemMacro[19].Kind == "layer-next" &&
    systemMacro[20].Kind == "layer-prev" && systemMacro[21].Kind == "layer-set" &&
    systemMacro[21].Number == 2);
var templateText = systemMacro[22].Text ?? "";
Check("система: подстановки", systemMacro[22].Kind == "text" &&
    templateText.Contains("{clipboard}") && templateText.Contains("{date}"));

var fileAction = new ActionSpec { Kind = ActionKind.Macro, Script = "press A", ScriptPath = "x.macro" };
Check("макрос: путь к файлу клонируется", fileAction.Clone().ScriptPath == "x.macro");

var textSpeed = MacroScript.Parse("text \"hello\" 100");
Check("текст: скорость 100", textSpeed.Count == 1 && textSpeed[0].Text == "hello" &&
    textSpeed[0].TypingDelayMs == 100);
var textInstant = MacroScript.Parse("text \"hello\" -1");
Check("текст: мгновенно", textInstant[0].TypingDelayMs == -1);
var textDefault = MacroScript.Parse("text \"hello\"");
Check("текст: скорость по умолчанию", textDefault[0].TypingDelayMs is null &&
    textDefault[0].Text == "hello");
var textNumeric = MacroScript.Parse("text 123");
Check("текст: число — это текст", textNumeric[0].Text == "123" &&
    textNumeric[0].TypingDelayMs is null);
var textQuotedNumeric = MacroScript.Parse("text \"hello 100\"");
Check("текст: число в кавычках", textQuotedNumeric[0].Text == "hello 100" &&
    textQuotedNumeric[0].TypingDelayMs is null);
var browserSpeed = MacroScript.Parse("browser find \"hello\" 100");
Check("browser: скорость find", browserSpeed[0].Key == "find" &&
    browserSpeed[0].TypingDelayMs == 100);
var browserSpeedWord = MacroScript.Parse("browser find \"speed\" 100");
Check("browser: слово speed как текст", browserSpeedWord[0].Key == "find" &&
    browserSpeedWord[0].Text == "speed" && browserSpeedWord[0].TypingDelayMs == 100);
var browserSpeedKeyword = MacroScript.Parse("browser page +1 speed 50");
Check("browser: скорость page", browserSpeedKeyword[0].Key == "page" &&
    browserSpeedKeyword[0].TypingDelayMs == 50);
var browserParamSpeed = MacroScript.Parse("browser set_param \"page\" \"287\" speed 25");
Check("browser: скорость set_param", browserParamSpeed[0].TypingDelayMs == 25);

var variables = MacroScript.Parse("""
    set counter = 5
    inc counter 2
    while counter < 10 {
        set counter = counter + 1
    }
    """);
Check("переменные: set", variables[0].Kind == "set" &&
    variables[0].Text == "counter" && variables[0].Text2 == "5");
Check("переменные: inc", variables[1].Kind == "inc" &&
    variables[1].Text == "counter" && variables[1].Number == 2);
Check("переменные: while", variables[2].Kind == "while" &&
    variables[2].Text == "counter < 10" && variables[2].Body?.Count == 1);
var whileBody = variables[2].Body!;
Check("переменные: тело while", whileBody[0].Kind == "set" &&
    whileBody[0].Text2 == "counter + 1");
var repeatWhile = MacroScript.Parse("repeat while counter < 5 {\n inc counter\n}");
Check("переменные: repeat while", repeatWhile.Count == 1 &&
    repeatWhile[0].Kind == "while" && repeatWhile[0].Text == "counter < 5" &&
    repeatWhile[0].Body?.Count == 1);

var mv = new MacroVariables();
Check("выражение: арифметика",
    MacroExpression.TryEvaluateValue("1 + 2 * 3", null, mv, out var arithmetic, out _) &&
    arithmetic.AsNumber() == 7);
mv.Set("counter", MacroValue.FromNumber(5));
Check("выражение: переменная и сравнение",
    MacroExpression.TryEvaluate("counter < 10", null, mv, out var compare, out _) && compare);
Check("выражение: арифметика с переменной",
    MacroExpression.TryEvaluate("counter + 1 == 6", null, mv, out var compare2, out _) && compare2);
mv.Set("name", MacroValue.FromString("nexus"));
Check("выражение: строка",
    MacroExpression.TryEvaluate("name == \"nexus\"", null, mv, out var stringEqual, out _) &&
    stringEqual);
Check("выражение: конкатенация",
    MacroExpression.TryEvaluateValue("name + \"-page\"", null, mv, out var joined, out _) &&
    joined.AsString() == "nexus-page");
Check("выражение: поле URL",
    MacroExpression.TryEvaluate("browser.url contains \"nexus\"",
        WinUrl("firefox", "Nexus", "https://www.nexusmods.com/mods"), mv, out var urlCond, out _) &&
    urlCond);
Check("выражение: неизвестная переменная",
    !MacroExpression.TryEvaluate("missing == 1", null, mv, out _, out _));

var blocks = MacroScript.Parse("""
    repeat 3 {
        press A
        if browser.url contains "nexus" {
            browser page +1
        } else {
            browser page -1
        }
    }
    """);
Check("блоки: repeat", blocks.Count == 1 && blocks[0].Kind == "repeat" &&
    blocks[0].Number == 3 && blocks[0].Body?.Count == 2);
var repeatBody = blocks[0].Body!;
var ifStep = repeatBody[1];
var ifBody = ifStep.Body!;
var elseBody = ifStep.ElseBody!;
Check("блоки: вложенный if", ifStep.Kind == "if" &&
    ifStep.Text == "browser.url contains \"nexus\"" &&
    ifBody.Count == 1 && elseBody.Count == 1);
Check("блоки: тело if", ifBody[0].Key == "page" &&
    ifBody[0].Number == 1 && elseBody[0].Number == -1);

// ---------------------------------------------------------------- psf
Check("кодирование A", PsfGenerator.EncodeLetterTrigger('A') == "700704",
    PsfGenerator.EncodeLetterTrigger('A'));
Check("кодирование M", PsfGenerator.EncodeLetterTrigger('M') == "700710",
    PsfGenerator.EncodeLetterTrigger('M'));

var vendorDir = PsfGenerator.FindVendorConfigDir();
Console.WriteLine("  ..   папка MBF1000PRO: " + (vendorDir ?? "не найдена"));
Check("папка MBF1000PRO найдена", vendorDir is not null);

if (vendorDir is not null)
{
    var template = Path.Combine(vendorDir, "default0.psf");
    var temp = Path.Combine(Path.GetTempPath(), "mm_selftest.psf");
    var generated = PsfGenerator.WriteProfile(template, temp);
    Check("psf создан", generated is not null && File.Exists(temp));

    if (generated is not null)
    {
        var bytes = File.ReadAllBytes(temp);
        Check("psf: BOM UTF-16", bytes.Length > 2 && bytes[0] == 0xFF && bytes[1] == 0xFE);
        var text = File.ReadAllText(temp, Encoding.Unicode);
        Check("psf: имя профиля", text.Contains("ProfileName=MouseMackros"));
        Check("psf: кнопка 1 = Key7 = A", text.Contains("Key7Value=700704"));
        Check("psf: кнопка 7 = Key14 = G", text.Contains("Key14Value=70070A"));
        Check("psf: кнопка 9 = Key13 = I", text.Contains("Key13Value=70070C"));
        Check("psf: кнопка 13 = Key1 = ПКМ", text.Contains("Key1Value=100200"));
        Check("psf: 16 кнопок", Enumerable.Range(0, 16).All(i => text.Contains($"Key{i}Value=")));
        Check("psf: слоты макросов сброшены",
            !text.Contains("MacroKeyIndex=1") && !text.Contains("MacroKeyIndex=2"));
        Check("psf: ЛКМ сохранена", text.Contains("Key0Value=100100"));

        var read = PsfReader.Read(temp);
        Check("psf reader: файл прочитан", read is not null);
        if (read is not null)
        {
            Check("psf reader: имя профиля", read.ProfileName == "MouseMackros", read.ProfileName);
            Check("psf reader: кнопка 1 = A",
                read.ForButton(1)?.Action is { Kind: ActionKind.Keys, Value: "Ctrl+Shift+Alt+A" },
                read.ForButton(1)?.Action?.Value ?? "null");
            Check("psf reader: кнопка 16 = M",
                read.ForButton(16)?.Action is { Kind: ActionKind.Keys, Value: "Ctrl+Shift+Alt+M" },
                read.ForButton(16)?.Action?.Value ?? "null");
            Check("psf reader: ЛКМ", read.ForButton(12)?.Combo == "mouse:Left",
                read.ForButton(12)?.Combo ?? "null");
            Check("psf reader: нейтральная раскладка", read.IsNeutralLayout);
        }

        File.Delete(temp);
    }

    var stock = PsfReader.Read(Path.Combine(vendorDir, "default0.psf"));
    Check("psf reader: стоковый профиль прочитан", stock is not null && stock.Keys.Count >= 16,
        stock?.Keys.Count.ToString() ?? "null");
    Check("psf reader: стоковая ЛКМ", stock?.ForButton(12)?.Combo == "mouse:Left",
        stock?.ForButton(12)?.Combo ?? "null");
}

// ---------------------------------------------------------------- vendor layouts -> actions
var vendorPsf = Path.Combine(Path.GetTempPath(), "mm_vendor_" + Guid.NewGuid().ToString("n") + ".psf");
var vendorText = """
    [Setting]
    ProfileName=Test
    Key9Name=Macro-tab right
    Key9Value=902101
    MacroKeyIndex=2
    MacroKey9Name=tab right
    MacroActionNum=6
    AllAction=A2000000A00000000900000009800000A0800000A2800000
    Key12Name=Macro-tab left
    Key12Value=902201
    MacroKeyIndex=1
    MacroKey12Name=tab left
    MacroActionNum=4
    AllAction=A20000000900000009800000A2800000
    Key3Name=Copy
    Key3Value=700106
    MacroKey3Name=
    MacroActionNum=0
    Key15Name=GamingMS.exe
    Key15URL=C:\Program Files\MBF1000PRO Mouse\GamingMS.exe
    Key15Value=600500
    MacroKey15Name=
    MacroActionNum=0
    Key14Name=Macro-open explorer
    Key14Value=902301
    MacroKey14Name=open explorer
    MacroActionNum=7
    AllAction=5B0000000300003E450000000300003F458000000300001F5B800000
    """;
File.WriteAllText(vendorPsf, vendorText, new UnicodeEncoding(false, true));

var vendor = PsfReader.Read(vendorPsf);
Check("vendor psf: прочитан", vendor is not null);
if (vendor is not null)
{
    Check("vendor psf: Copy = Ctrl+C",
        vendor.ForButton(15)?.Action is { Kind: ActionKind.Keys, Value: "Ctrl+C" },
        vendor.ForButton(15)?.Action?.Value ?? "null");

    var macro = vendor.ForButton(3)?.Action;
    Check("vendor psf: вкладка вправо → Ctrl+Tab",
        macro is { Kind: ActionKind.Keys, Value: "Ctrl+Tab" },
        $"{macro?.Kind} {macro?.Value}");
    Check("vendor psf: вкладка влево → Ctrl+Shift+Tab",
        vendor.ForButton(6)?.Action is { Kind: ActionKind.Keys, Value: "Ctrl+Shift+Tab" },
        $"{vendor.ForButton(6)?.Action?.Kind} {vendor.ForButton(6)?.Action?.Value}");
    Check("vendor psf: подпись макроса", vendor.ForButton(3)?.Label == "tab right",
        vendor.ForButton(3)?.Label ?? "null");
    Check("vendor psf: запуск приложения",
        vendor.ForButton(8)?.Action is { Kind: ActionKind.Run, Value: @"C:\Program Files\MBF1000PRO Mouse\GamingMS.exe" },
        vendor.ForButton(8)?.Action?.Value ?? "null");

    var explorer = vendor.ForButton(7)?.Action;
    Check("vendor psf: задержка в макросе",
        explorer is { Kind: ActionKind.Macro } && explorer.Script?.Contains("delay 62") == true,
        explorer?.Script ?? "null");
    Check("vendor psf: псевдокоманда задержки не превращается в клавишу",
        explorer?.Script?.Contains("VK03") != true,
        explorer?.Script ?? "null");

    var target = new Profile { Name = "Firefox" };
    int applied = vendor.ApplyTo(target);
    Check("vendor psf: действия перенесены", applied == 5, applied.ToString());
    Check("vendor psf: триггер кнопки 3 нейтральный",
        target.FindButton(3)?.Trigger == MouseButtonMap.TriggerFor(3),
        target.FindButton(3)?.Trigger ?? "null");
    Check("vendor psf: путь .psf записан", target.VendorProfilePath == vendorPsf);
    Check("vendor psf: ревизия декодера", target.PsfRevision == PsfReader.CurrentRevision,
        target.PsfRevision.ToString());
}

File.Delete(vendorPsf);

var userXz = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
    "Downloads", "xz.psf");
if (File.Exists(userXz))
{
    var xz = PsfReader.Read(userXz);
    Check("xz.psf: прочитан", xz is { Keys.Count: > 0 }, xz?.Keys.Count.ToString() ?? "null");
    Check("xz.psf: действия декодируются", xz?.Keys.Any(k => k.Action is not null) == true);
    Check("xz.psf: это не нейтральная раскладка", xz is { IsNeutralLayout: false });
}

// ---------------------------------------------------------------- profile storage
var dataDir = Path.Combine(Path.GetTempPath(), "mm_selftest_" + Guid.NewGuid().ToString("n"));
ProfileStore.UseDataDirectory(dataDir);

var stored = Presets.CreateDefault();
stored.Profiles[1].ApplicationPath = @"C:\Apps\browser.exe";
stored.Profiles[1].Applications.Add(@"C:\Apps\browser.exe");
stored.Profiles[1].Applications.Add(@"C:\Apps\second.exe");
stored.Profiles[1].VendorProfilePath = @"C:\psf\MouseMackros.psf";
stored.TypingDelayMs = 7;
stored.Profiles[1].TypingDelayMs = -1;
ProfileStore.Save(stored);

Check("storage: settings.json создан", File.Exists(Path.Combine(dataDir, "settings.json")));
Check("storage: профили в отдельных файлах",
    Directory.GetFiles(Path.Combine(dataDir, "profiles"), "*.json").Length == 2);
Check("storage: папка psf создаётся", Directory.CreateDirectory(ProfileStore.PsfDir).Exists);

var reloaded = ProfileStore.Load();
Check("storage: профили читаются", reloaded.Profiles.Count == 2, reloaded.Profiles.Count.ToString());
Check("storage: путь приложения сохранён",
    reloaded.Profiles.Any(p => p.ApplicationPath == @"C:\Apps\browser.exe"));
Check("storage: несколько приложений",
    reloaded.Profiles.Any(p => p.Applications.Count == 2));
Check("storage: процессы из приложений",
    reloaded.Profiles.Any(p => p.Match.Processes.Contains("second")));
Check("storage: путь .psf сохранён",
    reloaded.Profiles.Any(p => p.VendorProfilePath == @"C:\psf\MouseMackros.psf"));
Check("storage: скорость набора сохранена", reloaded.TypingDelayMs == 7,
    reloaded.TypingDelayMs.ToString());
Check("storage: скорость профиля сохранена",
    reloaded.Profiles.Any(p => p.TypingDelayMs == -1));
Check("storage: fallback восстановлен", reloaded.FallbackProfile.IsFallback);

var extraConfig = new AppConfig
{
    Profiles =
    {
        new Profile
        {
            Id = "extra", Name = "Extra",
            Bindings =
            {
                new MouseMackros.Model.Binding { Button = 0, Trigger = "wheel:Up" },
                new MouseMackros.Model.Binding { Button = 1, Trigger = MouseButtonMap.TriggerFor(1) }
            }
        }
    }
};
ProfileStore.Normalize(extraConfig);
Check("storage: привязки вне физических кнопок удаляются",
    extraConfig.Profiles[0].Bindings.Count == 1 && extraConfig.Profiles[0].Bindings[0].Button == 1,
    extraConfig.Profiles[0].Bindings.Count.ToString());

// legacy config.json
var legacyDir = Path.Combine(Path.GetTempPath(), "mm_selftest_legacy_" + Guid.NewGuid().ToString("n"));
Directory.CreateDirectory(legacyDir);
ProfileStore.UseDataDirectory(legacyDir);

var legacy = Presets.CreateDefault();
legacy.Profiles[0].Name = "Старый профиль";
File.WriteAllText(Path.Combine(legacyDir, "config.json"),
    JsonSerializer.Serialize(legacy, ProfileStore.JsonOptions), new UTF8Encoding(false));

var migrated = ProfileStore.Load();
Check("миграция: старый профиль переехал", migrated.Profiles.Count == 2, migrated.Profiles.Count.ToString());
Check("миграция: имя сохранилось", migrated.Profiles.Any(p => p.Name == "Старый профиль"));
Check("миграция: config.json заархивирован", File.Exists(Path.Combine(legacyDir, "config.json.migrated")));
Check("миграция: новые файлы созданы", File.Exists(Path.Combine(legacyDir, "settings.json")));

try
{
    Directory.Delete(dataDir, true);
    Directory.Delete(legacyDir, true);
}
catch
{
    // temp folders are allowed to stay
}

Console.WriteLine();
Console.WriteLine($"Итого: {passed} успешно, {failures} с ошибками");
return failures == 0 ? 0 : 1;
