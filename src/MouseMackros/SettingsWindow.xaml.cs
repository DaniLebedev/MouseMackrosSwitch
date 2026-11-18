using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using MouseMackros.Interop;
using MouseMackros.Services;

namespace MouseMackros;

public partial class SettingsWindow : Window
{
    private readonly Model.AppConfig _config;
    private string _hotkey;

    public SettingsWindow(Model.AppConfig config)
    {
        InitializeComponent();
        _config = config;
        _hotkey = config.ToggleHotkey;

        EnabledCheck.IsChecked = config.Enabled;
        ToastCheck.IsChecked = config.ShowToast;
        AutostartCheck.IsChecked = StartupService.IsEnabled();
        HotkeyBox.Text = config.ToggleHotkey;
        DataPathText.Text = ProfileStore.RootDir;
    }

    /// <summary>True when something was changed and saved.</summary>
    public bool Changed { get; private set; }

    private void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin)
            return;

        if (key == Key.Escape)
        {
            HotkeyBox.Text = _hotkey;
            Keyboard.ClearFocus();
            return;
        }

        var vk = KeyInterop.VirtualKeyFromKey(key);
        if (vk == 0) return;

        var mods = Keyboard.Modifiers;
        if (mods == ModifierKeys.None) return;

        var parts = new List<string>();
        if (mods.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (mods.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (mods.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (mods.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(KeyNames.GetName(vk));

        _hotkey = string.Join("+", parts);
        HotkeyBox.Text = _hotkey;
        Keyboard.ClearFocus();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _config.Enabled = EnabledCheck.IsChecked == true;
        _config.ShowToast = ToastCheck.IsChecked == true;
        _config.StartWithWindows = AutostartCheck.IsChecked == true;
        _config.ToggleHotkey = _hotkey;

        StartupService.Apply(_config.StartWithWindows);
        Changed = true;
        DialogResult = true;
        Close();
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo("explorer.exe", ProfileStore.RootDir) { UseShellExecute = true });

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
