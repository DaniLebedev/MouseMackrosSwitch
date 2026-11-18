using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using MouseMackros.Engine;
using MouseMackros.Input;
using MouseMackros.Interop;
using MouseMackros.Model;

namespace MouseMackros;

public sealed record BindingEditResult(string Trigger, string Label, ActionSpec Action);

public partial class BindingDialog : Window
{
    private readonly App _app;
    private string _trigger;
    private ActionSpec _action;

    private static readonly (ActionKind Kind, string Name)[] Kinds =
    {
        (ActionKind.Keys, "Нажать клавиши"),
        (ActionKind.Text, "Напечатать текст"),
        (ActionKind.Macro, "Макрос"),
        (ActionKind.Media, "Медиа-кнопка"),
        (ActionKind.Scroll, "Прокрутка колеса"),
        (ActionKind.Run, "Запустить программу"),
        (ActionKind.Profile, "Сменить профиль"),
        (ActionKind.Block, "Блокировать (ничего)"),
        (ActionKind.PassThrough, "Пропускать как есть")
    };

    private static readonly (string Value, string Name)[] Medias =
    {
        ("PlayPause", "Play / пауза"),
        ("Next", "Следующий трек"),
        ("Previous", "Предыдущий трек"),
        ("Stop", "Стоп"),
        ("VolumeUp", "Громче"),
        ("VolumeDown", "Тише"),
        ("VolumeMute", "Без звука")
    };

    private BindingDialog(App app, string trigger, string label, ActionSpec action, string title)
    {
        InitializeComponent();
        _app = app;
        _trigger = trigger;
        _action = action;
        TitleText.Text = title;

        foreach (var (kind, name) in Kinds) KindBox.Items.Add(new ComboBoxItem { Content = name, Tag = kind });
        foreach (var (value, name) in Medias) MediaBox.Items.Add(new ComboBoxItem { Content = name, Tag = value });
        MacroModeBox.Items.Add(new ComboBoxItem { Content = "Один раз", Tag = MacroMode.Once });
        MacroModeBox.Items.Add(new ComboBoxItem { Content = "Пока кнопка зажата", Tag = MacroMode.Hold });
        MacroModeBox.Items.Add(new ComboBoxItem { Content = "Вкл / выкл по нажатию", Tag = MacroMode.Toggle });
        ProfileBox.ItemsSource = app.Config.Profiles;
        MacroHelp.Text = MacroScript.ToHelpText();

        LabelBox.Text = label ?? "";
        FillFromAction(action);
        UpdateTriggerText();
    }

    public BindingEditResult? Result { get; private set; }

    public static BindingEditResult? Edit(Window owner, App app, string trigger, string label, ActionSpec action,
        string title)
    {
        var dialog = new BindingDialog(app, trigger, label, action, title) { Owner = owner };
        return dialog.ShowDialog() == true ? dialog.Result : null;
    }

    private void UpdateTriggerText()
    {
        TriggerText.Text = Trigger.Parse(_trigger).DisplayName;
    }

    private void FillFromAction(ActionSpec action)
    {
        for (int i = 0; i < KindBox.Items.Count; i++)
        {
            if (KindBox.Items[i] is ComboBoxItem { Tag: ActionKind kind } && kind == action.Kind)
            {
                KindBox.SelectedIndex = i;
                break;
            }
        }
        if (KindBox.SelectedIndex < 0) KindBox.SelectedIndex = 0;

        KeysBox.Text = action.Kind == ActionKind.Keys ? action.Value : "";
        TextBoxValue.Text = action.Kind == ActionKind.Text ? action.Value : "";
        MacroBox.Text = action.Script ?? "";
        RepeatBox.Text = action.Repeat.ToString();

        foreach (ComboBoxItem item in MacroModeBox.Items)
        {
            if (item.Tag is MacroMode mode && mode == action.Mode)
            {
                MacroModeBox.SelectedItem = item;
                break;
            }
        }
        if (MacroModeBox.SelectedIndex < 0) MacroModeBox.SelectedIndex = 0;

        var mediaValue = action.Kind == ActionKind.Media ? action.Value : "PlayPause";
        foreach (ComboBoxItem item in MediaBox.Items)
        {
            if (item.Tag is string value && value == mediaValue)
            {
                MediaBox.SelectedItem = item;
                break;
            }
        }
        if (MediaBox.SelectedIndex < 0) MediaBox.SelectedIndex = 0;

        RunBox.Text = action.Kind == ActionKind.Run ? action.Value : "";
        ArgsBox.Text = action.Arguments ?? "";
        ScrollBox.Text = action.Kind == ActionKind.Scroll && action.Value.Length > 0 ? action.Value : "1";
        ProfileBox.SelectedValue = action.Kind == ActionKind.Profile && action.Value.Length > 0
            ? action.Value
            : _app.Config.DefaultProfileId;

        UpdatePanels();
    }

    private ActionKind SelectedKind =>
        KindBox.SelectedItem is ComboBoxItem { Tag: ActionKind kind } ? kind : ActionKind.Keys;

    private void KindBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdatePanels();

    private void UpdatePanels()
    {
        if (KeysPanel is null) return;
        var kind = SelectedKind;
        KeysPanel.Visibility = kind == ActionKind.Keys ? Visibility.Visible : Visibility.Collapsed;
        TextPanel.Visibility = kind == ActionKind.Text ? Visibility.Visible : Visibility.Collapsed;
        MacroPanel.Visibility = kind == ActionKind.Macro ? Visibility.Visible : Visibility.Collapsed;
        MediaPanel.Visibility = kind == ActionKind.Media ? Visibility.Visible : Visibility.Collapsed;
        RunPanel.Visibility = kind == ActionKind.Run ? Visibility.Visible : Visibility.Collapsed;
        ProfilePanel.Visibility = kind == ActionKind.Profile ? Visibility.Visible : Visibility.Collapsed;
        ScrollPanel.Visibility = kind == ActionKind.Scroll ? Visibility.Visible : Visibility.Collapsed;
    }

    private void KeysBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin or Key.None)
            return;
        if (key == Key.Tab) return;

        var vk = KeyInterop.VirtualKeyFromKey(key);
        if (vk == 0) return;

        var mods = Keyboard.Modifiers;
        var names = new List<string>();
        if (mods.HasFlag(ModifierKeys.Control)) names.Add("Ctrl");
        if (mods.HasFlag(ModifierKeys.Shift)) names.Add("Shift");
        if (mods.HasFlag(ModifierKeys.Alt)) names.Add("Alt");
        if (mods.HasFlag(ModifierKeys.Windows)) names.Add("Win");
        names.Add(KeyNames.GetName(vk));
        KeysBox.Text = string.Join("+", names);
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Выберите программу",
            Filter = "Программы (*.exe;*.bat;*.cmd;*.lnk)|*.exe;*.bat;*.cmd;*.lnk|Все файлы (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) == true) RunBox.Text = dialog.FileName;
    }

    private void Recapture_Click(object sender, RoutedEventArgs e)
    {
        var trigger = TriggerLearnDialog.Capture(this, _app.Engine, _app.Config.FallbackProfile);
        if (trigger is null) return;
        _trigger = trigger.ToString();
        UpdateTriggerText();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var kind = SelectedKind;
        var action = new ActionSpec { Kind = kind };

        switch (kind)
        {
            case ActionKind.Keys:
                if (string.IsNullOrWhiteSpace(KeysBox.Text) ||
                    !KeyNames.TryParseCombo(KeysBox.Text, out _, out _))
                {
                    AppDialog.ShowError("Сначала нажмите нужное сочетание клавиш в поле «Сочетание клавиш».");
                    return;
                }
                action.Value = KeyNames.NormalizeCombo(KeysBox.Text);
                break;

            case ActionKind.Text:
                action.Value = TextBoxValue.Text;
                break;

            case ActionKind.Macro:
                action.Script = MacroBox.Text;
                action.Mode = MacroModeBox.SelectedItem is ComboBoxItem { Tag: MacroMode mode } ? mode : MacroMode.Once;
                action.Repeat = int.TryParse(RepeatBox.Text, out var rep) ? Math.Clamp(rep, 0, 9999) : 0;
                if (MacroScript.Parse(action.Script).Count == 0)
                {
                    AppDialog.ShowError("Макрос пустой. Добавьте хотя бы одну команду.");
                    return;
                }
                break;

            case ActionKind.Media:
                action.Value = MediaBox.SelectedItem is ComboBoxItem { Tag: string m } ? m : "PlayPause";
                break;

            case ActionKind.Run:
                if (string.IsNullOrWhiteSpace(RunBox.Text))
                {
                    AppDialog.ShowError("Укажите программу, файл или ссылку.");
                    return;
                }
                action.Value = RunBox.Text.Trim();
                action.Arguments = string.IsNullOrWhiteSpace(ArgsBox.Text) ? null : ArgsBox.Text.Trim();
                break;

            case ActionKind.Profile:
                action.Value = ProfileBox.SelectedValue as string ?? "";
                if (action.Value.Length == 0)
                {
                    AppDialog.ShowError("Выберите профиль.");
                    return;
                }
                break;

            case ActionKind.Scroll:
                action.Value = int.TryParse(ScrollBox.Text, out var ticks) ? ticks.ToString() : "1";
                break;
        }

        Result = new BindingEditResult(_trigger, LabelBox.Text.Trim(), action);
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
