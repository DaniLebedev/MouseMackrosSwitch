using MouseMackros.Input;
using MouseMackros.Model;

namespace MouseMackros.Services;

/// <summary>
/// Ready made profiles. Triggers Ctrl+Alt+Shift+A..M are the ones written into the
/// MBF1000PRO profile by <see cref="PsfGenerator"/>.
/// </summary>
public static class Presets
{
    public const string TriggerPrefix = "Ctrl+Alt+Shift+";

    public static readonly string[] TriggerLetters =
        { "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M" };

    public static string TriggerFor(int index) =>
        Trigger.Key(TriggerPrefix + TriggerLetters[index % TriggerLetters.Length]).ToString();

    public static AppConfig CreateDefault()
    {
        var config = new AppConfig();
        config.Profiles.Add(CreateDefaultProfile());
        config.Profiles.Add(CreateBrowser());
        config.Profiles.Add(CreateGames());
        config.Profiles.Add(CreateMedia());
        return config;
    }

    public static Profile CreateDefaultProfile()
    {
        var p = new Profile
        {
            Id = "default",
            Name = "Везде (по умолчанию)",
            Accent = "#7C8DA6",
            IsFallback = true,
            Match = new MatchRules { Priority = -100 }
        };
        p.Bindings.Add(Make("mouse:X1", ActionKind.Keys, "Alt+Left", "Боковая 1 — назад"));
        p.Bindings.Add(Make("mouse:X2", ActionKind.Keys, "Alt+Right", "Боковая 2 — вперёд"));
        return p;
    }

    public static Profile CreateBrowser()
    {
        var p = new Profile
        {
            Id = "browser",
            Name = "Браузер",
            Accent = "#4C9AFF",
            Match = new MatchRules
            {
                Processes =
                {
                    "chrome", "msedge", "firefox", "brave", "opera", "vivaldi", "browser",
                    "yandex", "librewolf", "thorium", "arc"
                },
                Priority = 20
            }
        };

        var items = new (int idx, string combo, string label)[]
        {
            (0, "Alt+Left", "Назад"),
            (1, "Alt+Right", "Вперёд"),
            (2, "Ctrl+Tab", "Вкладка вправо"),
            (3, "Ctrl+Shift+Tab", "Вкладка влево"),
            (4, "Ctrl+T", "Новая вкладка"),
            (5, "Ctrl+W", "Закрыть вкладку"),
            (6, "Ctrl+Shift+T", "Вернуть вкладку"),
            (7, "F5", "Обновить страницу"),
            (8, "Ctrl+F", "Поиск на странице"),
            (9, "Ctrl+L", "Адресная строка"),
            (10, "F11", "Полный экран"),
            (11, "Ctrl+Oem=", "Масштаб +"),
            (12, "Ctrl+Oem-", "Масштаб −")
        };

        foreach (var (idx, combo, label) in items)
        {
            p.Bindings.Add(new Binding
            {
                Trigger = TriggerFor(idx),
                Label = label,
                Action = new ActionSpec { Kind = ActionKind.Keys, Value = combo }
            });
        }

        p.Bindings.Add(Make("mouse:X1", ActionKind.Keys, "Alt+Left", "Боковая 1 — назад"));
        p.Bindings.Add(Make("mouse:X2", ActionKind.Keys, "Alt+Right", "Боковая 2 — вперёд"));
        return p;
    }

    public static Profile CreateGames()
    {
        var p = new Profile
        {
            Id = "games",
            Name = "Игры",
            Accent = "#F0645A",
            Match = new MatchRules { Priority = 30 }
        };

        for (int i = 0; i < 9; i++)
        {
            p.Bindings.Add(new Binding
            {
                Trigger = TriggerFor(i),
                Label = $"Слот {i + 1}",
                Action = new ActionSpec { Kind = ActionKind.Keys, Value = (i + 1).ToString() }
            });
        }

        var extra = new (string combo, string label)[]
        {
            ("Q", "Способность Q"),
            ("E", "Способность E"),
            ("R", "Способность R"),
            ("F", "Способность F"),
            ("Shift", "Бег"),
            ("Space", "Прыжок"),
            ("Ctrl", "Присесть"),
            ("Tab", "Карта / счёт"),
            ("Esc", "Меню"),
            ("G", "Граната"),
            ("V", "Ближний бой"),
            ("B", "Магазин"),
            ("T", "Чат-колесо")
        };
        for (int i = 0; i < extra.Length; i++)
        {
            p.Bindings.Add(new Binding
            {
                Trigger = TriggerFor(9 + i),
                Label = extra[i].label,
                Action = new ActionSpec { Kind = ActionKind.Keys, Value = extra[i].combo }
            });
        }

        // Example of a one-click macro (shows the syntax in the editor).
        p.Bindings.Add(new Binding
        {
            Trigger = "wheel:Up",
            Label = "Пример макроса: рывок",
            Action = new ActionSpec
            {
                Kind = ActionKind.Macro,
                Mode = MacroMode.Once,
                Repeat = 0,
                Script = "# Нажмите «Тест», чтобы попробовать\npress Space\n delay 60\n press Shift\n delay 200"
            }
        });

        return p;
    }

    public static Profile CreateMedia()
    {
        var p = new Profile
        {
            Id = "media",
            Name = "Музыка и видео",
            Accent = "#9B6BF2",
            Match = new MatchRules
            {
                Processes = { "spotify", "vlc", "aimp", "musicbee", "foobar2000", "wmplayer", "mpc-hc64", "mpc-be64" },
                Priority = 10
            }
        };

        var items = new (string media, string label)[]
        {
            ("PlayPause", "Play / пауза"),
            ("Next", "Следующий трек"),
            ("Previous", "Предыдущий трек"),
            ("Stop", "Стоп"),
            ("VolumeUp", "Громче"),
            ("VolumeDown", "Тише"),
            ("VolumeMute", "Без звука")
        };

        for (int i = 0; i < items.Length; i++)
        {
            p.Bindings.Add(new Binding
            {
                Trigger = TriggerFor(i),
                Label = items[i].label,
                Action = new ActionSpec { Kind = ActionKind.Media, Value = items[i].media }
            });
        }

        return p;
    }

    private static Binding Make(string trigger, ActionKind kind, string value, string label) => new()
    {
        Trigger = trigger,
        Label = label,
        Action = new ActionSpec { Kind = kind, Value = value }
    };
}
