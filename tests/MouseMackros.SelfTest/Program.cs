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
Check("storage: fallback восстановлен", reloaded.FallbackProfile.IsFallback);

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
