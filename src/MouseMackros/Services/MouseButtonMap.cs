using MouseMackros.Input;

namespace MouseMackros.Services;

/// <summary>
/// Physical button numbers of the MBF1000PRO as the vendor software shows them,
/// and the .psf slot / trigger letter each editable button is wired to.
///
/// Buttons 12, 13 and 14 are the main left / right / middle buttons. The vendor
/// utility does not let the user remap them, so they are not editable here.
/// The other 13 buttons get Ctrl+Alt+Shift+A..M from <see cref="PsfGenerator"/>.
///
/// Verified against the vendor config: button -&gt; KeyN
///   1..6   -&gt; Key7, Key8, Key9, Key10, Key11, Key12
///   7..9   -&gt; Key14, Key15, Key13
///   10,11  -&gt; Key5, Key6
///   12..14 -&gt; Key0, Key1, Key2   (main buttons)
///   15,16  -&gt; Key3, Key4
/// </summary>
public static class MouseButtonMap
{
    public sealed record ButtonInfo(int Number, int KeyIndex, char? Letter, string Name, bool Editable);

    /// <summary>All 16 buttons in vendor order.</summary>
    public static readonly IReadOnlyList<ButtonInfo> All = new List<ButtonInfo>
    {
        new(1, 7, 'A', "Кнопка 1", true),
        new(2, 8, 'B', "Кнопка 2", true),
        new(3, 9, 'C', "Кнопка 3", true),
        new(4, 10, 'D', "Кнопка 4", true),
        new(5, 11, 'E', "Кнопка 5", true),
        new(6, 12, 'F', "Кнопка 6", true),
        new(7, 14, 'G', "Кнопка 7", true),
        new(8, 15, 'H', "Кнопка 8", true),
        new(9, 13, 'I', "Кнопка 9", true),
        new(10, 5, 'J', "Кнопка 10", true),
        new(11, 6, 'K', "Кнопка 11", true),
        new(12, 0, null, "Левая (ЛКМ)", false),
        new(13, 1, null, "Правая (ПКМ)", false),
        new(14, 2, null, "Средняя (СКМ)", false),
        new(15, 3, 'L', "Кнопка 15", true),
        new(16, 4, 'M', "Кнопка 16", true)
    };

    /// <summary>Buttons the user can assign an action to.</summary>
    public static IEnumerable<ButtonInfo> Editable => All.Where(b => b.Editable);

    public static ButtonInfo? Get(int number) => All.FirstOrDefault(b => b.Number == number);

    public static ButtonInfo? GetByKeyIndex(int keyIndex) => All.FirstOrDefault(b => b.KeyIndex == keyIndex);

    public static ButtonInfo? GetByLetter(char letter) =>
        Editable.FirstOrDefault(b => b.Letter == char.ToUpperInvariant(letter));

    /// <summary>Serialized trigger ("key:Ctrl+Shift+Alt+A") for an editable button.</summary>
    public static string TriggerFor(int number)
    {
        var info = Get(number);
        if (info?.Letter is null) return "";
        return Trigger.Key("Ctrl+Alt+Shift+" + info.Letter).ToString();
    }

    /// <summary>
    /// Maps a captured trigger back to a button number. Returns 0 for anything
    /// that is not one of the generated Ctrl+Alt+Shift+A..M combinations.
    /// </summary>
    public static int ButtonForTrigger(string? triggerText)
    {
        var trigger = Trigger.Parse(triggerText);
        if (trigger.Kind != TriggerKind.Key) return 0;

        if (!Interop.KeyNames.TryParseCombo(trigger.Value, out var modifiers, out int key)) return 0;

        var normalized = modifiers.Select(Interop.KeyNames.NormalizeModifier).Distinct().OrderBy(m => m).ToList();
        if (normalized.Count != 3 || normalized[0] != 0 || normalized[1] != 1 || normalized[2] != 2) return 0;
        if (key < 0x41 || key > 0x4D) return 0; // A..M

        return GetByLetter((char)key)?.Number ?? 0;
    }

    /// <summary>
    /// Fills in <see cref="Model.Binding.Button"/> for older configs and moves
    /// buttons stored under the wrong number after a mapping fix.
    /// </summary>
    public static void NormalizeBindings(Model.Profile profile)
    {
        foreach (var binding in profile.Bindings)
        {
            if (binding.Button <= 0)
            {
                var byTrigger = ButtonForTrigger(binding.Trigger);
                if (byTrigger > 0) binding.Button = byTrigger;
            }
        }
    }
}
