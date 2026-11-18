using MouseMackros.Input;
using MouseMackros.Model;

namespace MouseMackros.Services;

/// <summary>
/// First run profiles. Only a fallback profile and one browser example are
/// created — the user builds the rest from the button list.
/// </summary>
public static class Presets
{
    public const string TriggerPrefix = "Ctrl+Alt+Shift+";

    public static AppConfig CreateDefault()
    {
        var config = new AppConfig();
        config.DefaultProfileId = "default";
        config.Profiles.Add(CreateDefaultProfile());
        config.Profiles.Add(CreateBrowser());
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

        p.Bindings.Add(Button(1, ActionKind.Keys, "Alt+Left", "Назад"));
        p.Bindings.Add(Button(2, ActionKind.Keys, "Alt+Right", "Вперёд"));
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
                    "chrome", "msedge", "firefox", "brave", "opera", "vivaldi",
                    "yandex", "librewolf", "thorium", "arc"
                },
                Priority = 20
            }
        };

        var items = new (int button, string combo, string label)[]
        {
            (1, "Alt+Left", "Назад"),
            (2, "Alt+Right", "Вперёд"),
            (3, "Ctrl+Tab", "Вкладка вправо"),
            (4, "Ctrl+Shift+Tab", "Вкладка влево"),
            (5, "Ctrl+T", "Новая вкладка"),
            (6, "Ctrl+W", "Закрыть вкладку"),
            (7, "Ctrl+Shift+T", "Вернуть вкладку"),
            (8, "F5", "Обновить страницу"),
            (9, "Ctrl+F", "Поиск на странице"),
            (10, "Ctrl+L", "Адресная строка"),
            (11, "F11", "Полный экран")
        };

        foreach (var (button, combo, label) in items)
            p.Bindings.Add(Button(button, ActionKind.Keys, combo, label));

        // Sample macro, kept as documentation of the syntax.
        p.Bindings.Add(new Binding
        {
            Button = 15,
            Trigger = MouseButtonMap.TriggerFor(15),
            Label = "Пример макроса",
            Action = new ActionSpec
            {
                Kind = ActionKind.Macro,
                Mode = MacroMode.Once,
                Script = "# Пример макроса: двойной клик\npress mouse:left\n delay 80\n press mouse:left"
            }
        });

        return p;
    }

    private static Binding Button(int number, ActionKind kind, string value, string label) => new()
    {
        Button = number,
        Trigger = MouseButtonMap.TriggerFor(number),
        Label = label,
        Action = new ActionSpec { Kind = kind, Value = value }
    };

    /// <summary>Trigger for an editable button, used by <see cref="PsfGenerator"/>.</summary>
    public static string TriggerForButton(int number) => MouseButtonMap.TriggerFor(number);
}
