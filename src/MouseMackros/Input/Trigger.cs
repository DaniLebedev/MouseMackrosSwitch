namespace MouseMackros.Input;

/// <summary>Kinds of input our trigger system understands.</summary>
public enum TriggerKind
{
    Key,
    MouseButton,
    Wheel,
    None
}

/// <summary>
/// A single bindable input. Serialized as a short string:
///   key:Ctrl+Alt+Shift+A | mouse:X1 | wheel:Up
/// </summary>
public sealed record Trigger(TriggerKind Kind, string Value)
{
    public static readonly Trigger None = new(TriggerKind.None, "");

    public static Trigger Key(string combo) => new(TriggerKind.Key, Interop.KeyNames.NormalizeCombo(combo));
    public static Trigger Mouse(string button) => new(TriggerKind.MouseButton, button);
    public static Trigger Wheel(string direction) => new(TriggerKind.Wheel, direction);

    public override string ToString() => Kind switch
    {
        TriggerKind.Key => "key:" + Value,
        TriggerKind.MouseButton => "mouse:" + Value,
        TriggerKind.Wheel => "wheel:" + Value,
        _ => ""
    };

    public static Trigger Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return None;
        var idx = text.IndexOf(':');
        if (idx < 0) return None;
        var kind = text[..idx].Trim().ToLowerInvariant();
        var value = text[(idx + 1)..].Trim();
        if (value.Length == 0) return None;
        return kind switch
        {
            "key" => Key(value),
            "mouse" => Mouse(value),
            "wheel" => Wheel(value),
            _ => None
        };
    }

    public string DisplayName => Kind switch
    {
        TriggerKind.Key => Interop.KeyNames.DescribeCombo(Value),
        TriggerKind.MouseButton => Value switch
        {
            "Left" => "ЛКМ",
            "Right" => "ПКМ",
            "Middle" => "СКМ",
            "X1" => "Боковая 1 (назад)",
            "X2" => "Боковая 2 (вперёд)",
            _ => Value
        },
        TriggerKind.Wheel => Value switch
        {
            "Up" => "Колесо вверх",
            "Down" => "Колесо вниз",
            "Left" => "Колесо влево",
            "Right" => "Колесо вправо",
            _ => Value
        },
        _ => "—"
    };

    public bool IsEmpty => Kind == TriggerKind.None;
}
