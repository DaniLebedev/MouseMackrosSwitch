using System.Diagnostics;
using System.Drawing;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MouseMackros.Services;

/// <summary>An application the user can attach a profile to.</summary>
public sealed class AppEntry
{
    public string Name { get; init; } = "";
    public string Path { get; init; } = "";
    public string Subtitle { get; init; } = "";
    public string ProcessName { get; init; } = "";
    public ImageSource? Icon { get; init; }

    public string Initial => string.IsNullOrWhiteSpace(Name) ? "?" : Name[..1].ToUpperInvariant();
}

/// <summary>Application icons, running processes and Start Menu programs.</summary>
public static class AppCatalog
{
    private static readonly Dictionary<string, ImageSource?> IconCache = new(StringComparer.OrdinalIgnoreCase);

    public static ImageSource? IconFor(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        if (IconCache.TryGetValue(path, out var cached)) return cached;

        ImageSource? result = null;
        try
        {
            if (File.Exists(path))
            {
                using var icon = Icon.ExtractAssociatedIcon(path);
                if (icon is not null)
                {
                    var source = Imaging.CreateBitmapSourceFromHIcon(
                        icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    source.Freeze();
                    result = source;
                }
            }
        }
        catch
        {
            // broken or inaccessible executable - fall back to the letter badge
        }

        IconCache[path] = result;
        return result;
    }

    public static List<AppEntry> Running()
    {
        var result = new List<AppEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                string? path = null;
                try
                {
                    path = process.MainModule?.FileName;
                }
                catch
                {
                    // 64-bit / protected / packaged processes
                }

                if (string.Equals(process.ProcessName, "MouseMackros", StringComparison.OrdinalIgnoreCase)) continue;
                if (IsBackgroundHelper(process.ProcessName)) continue;

                bool hasWindow = process.MainWindowHandle != IntPtr.Zero ||
                                 !string.IsNullOrWhiteSpace(process.MainWindowTitle);
                bool validPath = !string.IsNullOrWhiteSpace(path) &&
                                 path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);

                if (validPath && path!.StartsWith(Environment.SystemDirectory, StringComparison.OrdinalIgnoreCase) &&
                    !hasWindow)
                    continue;
                if (!validPath && !hasWindow) continue;

                var key = validPath ? path! : "process:" + process.ProcessName;
                if (!seen.Add(key)) continue;

                var name = validPath ? SafeDescription(process, path!) : process.ProcessName;
                result.Add(new AppEntry
                {
                    Name = name,
                    Path = validPath ? path! : "",
                    Subtitle = validPath ? path! : "запущенная программа",
                    ProcessName = process.ProcessName,
                    Icon = validPath ? IconFor(path!) : null
                });
            }
            catch
            {
                // process disappeared
            }
            finally
            {
                process.Dispose();
            }
        }

        return result
            .OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static bool IsBackgroundHelper(string processName)
    {
        var name = processName.ToLowerInvariant();
        return name.Contains("crash") || name.Contains("helper") || name.Contains("service")
               || name.Contains("gameinput")
               || name is "werfault" or "runtimebroker" or "dllhost" or "conhost"
                   or "svchost" or "sihost" or "taskhostw" or "ctfmon"
                   or "searchhost" or "shellexperiencehost" or "textinputhost" or "video.ui"
                   or "msmpeng" or "securityhealthsystray" or "smartscreen" or "searchindexer"
                   or "backgroundtaskhost" or "widgets" or "phoneexperiencehost"
                   or "applicationframehost" or "lockapp" or "useroobebroker";
    }

    private static string SafeDescription(Process process, string path)
    {
        try
        {
            var description = process.MainModule?.FileVersionInfo.FileDescription;
            if (!string.IsNullOrWhiteSpace(description)) return description.Trim();
        }
        catch
        {
            // ignore
        }

        return Path.GetFileNameWithoutExtension(path);
    }

    public static List<AppEntry> Installed()
    {
        var stopwatch = Stopwatch.StartNew();
        var result = new List<AppEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var directory in StartMenuDirectories())
        {
            if (!Directory.Exists(directory)) continue;

            List<string> files;
            try
            {
                // IgnoreInaccessible: some Start Menu subfolders are ACL-protected,
                // and one of them must not hide the whole catalog.
                files = Directory.EnumerateFiles(directory, "*.lnk", new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = true,
                    AttributesToSkip = FileAttributes.ReparsePoint
                }).ToList();
            }
            catch
            {
                continue;
            }

            foreach (var link in files)
            {
                try
                {
                    var linkName = Path.GetFileNameWithoutExtension(link);
                    if (IsNotApplicationLink(linkName)) continue;

                    var target = ResolveShortcut(link);
                    if (string.IsNullOrWhiteSpace(target)) continue;
                    if (!target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!File.Exists(target)) continue;
                    if (target.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase)) continue;
                    if (target.Contains(@"\Uninstall", StringComparison.OrdinalIgnoreCase)) continue;
                    if (target.Contains(@"\Windows\Installer\", StringComparison.OrdinalIgnoreCase)) continue;

                    var fileName = Path.GetFileName(target);
                    if (fileName.StartsWith("unins", StringComparison.OrdinalIgnoreCase) ||
                        fileName.Contains("uninstall", StringComparison.OrdinalIgnoreCase) ||
                        fileName.Contains("uninst", StringComparison.OrdinalIgnoreCase) ||
                        fileName.Equals("msiexec.exe", StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (!seen.Add(target)) continue;

                    result.Add(new AppEntry
                    {
                        Name = linkName,
                        Path = target,
                        Subtitle = target,
                        ProcessName = Path.GetFileNameWithoutExtension(target),
                        Icon = IconFor(target)
                    });
                }
                catch
                {
                    // broken shortcut
                }
            }
        }

        var ordered = result
            .OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        ProfileStore.Log($"Каталог программ: {ordered.Count} шт. за {stopwatch.ElapsedMilliseconds} мс");
        return ordered;
    }

    private static IEnumerable<string> StartMenuDirectories()
    {
        yield return Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.StartMenu);
    }

    private static bool IsNotApplicationLink(string name)
    {
        var lower = name.ToLowerInvariant();
        if (name.EndsWith("...") || name.EndsWith('…')) return true;

        return lower.StartsWith("деинсталлировать") || lower.StartsWith("деинсталляция")
               || lower.StartsWith("удалить") || lower.StartsWith("удаление")
               || lower.StartsWith("uninstall") || lower.StartsWith("remove")
               || lower.Contains("readme") || lower.Contains("справка")
               || lower.Contains("документация");
    }

    private static string? ResolveShortcut(string link)
    {
        try
        {
            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (type is null) return null;

            dynamic shell = Activator.CreateInstance(type)!;
            dynamic shortcut = shell.CreateShortcut(link);
            string target = shortcut.TargetPath as string ?? "";
            return string.IsNullOrWhiteSpace(target) ? null : target;
        }
        catch
        {
            return null;
        }
    }
}
