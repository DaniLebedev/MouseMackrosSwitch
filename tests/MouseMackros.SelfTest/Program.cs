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
Check("4 профиля по умолчанию", config.Profiles.Count == 4, config.Profiles.Count.ToString());
Check("резервный профиль = default", config.FallbackProfile.Id == "default", config.FallbackProfile.Id);
Check("в браузере 15 действий", config.Profiles[1].Bindings.Count == 15,
    config.Profiles[1].Bindings.Count.ToString());

// ---------------------------------------------------------------- json round trip
var json = JsonSerializer.Serialize(config, ConfigService.JsonOptions);
var back = JsonSerializer.Deserialize<AppConfig>(json, ConfigService.JsonOptions)!;
Check("JSON: профили сохраняются", back.Profiles.Count == config.Profiles.Count);
Check("JSON: действия сохраняются",
    back.Profiles[1].Bindings.Count == config.Profiles[1].Bindings.Count);
Check("JSON: тип действия читается",
    back.Profiles[1].Bindings[0].Action.Kind == ActionKind.Keys,
    back.Profiles[1].Bindings[0].Action.Kind.ToString());
Check("JSON: триггер сохраняется",
    back.Profiles[1].Bindings[0].Trigger == config.Profiles[1].Bindings[0].Trigger,
    back.Profiles[1].Bindings[0].Trigger);

// ---------------------------------------------------------------- wildcard matching
Check("wildcard: chrome == chrome", ProfileEngine.Wildcard("chrome", "chrome"));
Check("wildcard: Chrome регистронезависим", ProfileEngine.Wildcard("Chrome", "chrome"));
Check("wildcard: *game*", ProfileEngine.Wildcard("*game*", "mygameclient"));
Check("wildcard: game.exe", ProfileEngine.Wildcard("game.exe", "game"));
Check("wildcard: нет ложного совпадения", !ProfileEngine.Wildcard("chrome", "msedge"));

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
        Check("psf: триггер A в слоте 3", text.Contains("Key3Value=700704"));
        Check("psf: триггер M в слоте 15", text.Contains("Key15Value=700710"));
        Check("psf: ЛКМ сохранена", text.Contains("Key0Value=100100"));
        Check("psf: 16 кнопок", Enumerable.Range(0, 16).All(i => text.Contains($"Key{i}Value=")));
        File.Delete(temp);
    }
}

// ---------------------------------------------------------------- config load
var loaded = ConfigService.Load();
Check("конфиг читается", loaded.Profiles.Count > 0, loaded.Profiles.Count.ToString());
Check("горячая клавиша паузы", !string.IsNullOrWhiteSpace(loaded.ToggleHotkey), loaded.ToggleHotkey);

Console.WriteLine();
Console.WriteLine($"Итого: {passed} успешно, {failures} с ошибками");
return failures == 0 ? 0 : 1;
