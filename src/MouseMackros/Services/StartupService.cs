using Microsoft.Win32;

namespace MouseMackros.Services;

public static class StartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "MouseMackros";

    private static string CommandLine
    {
        get
        {
            var exe = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "MouseMackros.exe");
            return $"\"{exe}\" --silent";
        }
    }

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string s && s.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    public static void Apply(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (key is null) return;
            if (enabled) key.SetValue(ValueName, CommandLine);
            else key.DeleteValue(ValueName, false);
        }
        catch (Exception ex)
        {
            ConfigService.Log("Автозапуск: " + ex.Message);
        }
    }
}
