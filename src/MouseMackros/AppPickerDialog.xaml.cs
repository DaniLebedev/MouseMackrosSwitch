using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using MouseMackros.Services;

namespace MouseMackros;

/// <summary>Picks an .exe from running processes, Start Menu programs or disk.</summary>
public partial class AppPickerDialog : Window
{
    private readonly List<AppEntry> _running;
    private List<AppEntry>? _installed;
    private bool _showRunning = true;
    private bool _loadingInstalled;

    private AppPickerDialog(string? currentPath)
    {
        InitializeComponent();
        _running = AppCatalog.Running();
        ApplyFilter();

        if (!string.IsNullOrWhiteSpace(currentPath))
        {
            SearchBox.Text = Path.GetFileNameWithoutExtension(currentPath);
            SearchBox.SelectAll();
        }

        Loaded += (_, _) => SearchBox.Focus();
    }

    public AppEntry? Result { get; private set; }

    public static AppEntry? Pick(Window owner, string? currentPath = null)
    {
        var dialog = new AppPickerDialog(currentPath) { Owner = owner };
        return dialog.ShowDialog() == true ? dialog.Result : null;
    }

    private void ApplyFilter()
    {
        var source = _showRunning ? _running : _installed ?? new List<AppEntry>();
        var query = SearchBox?.Text?.Trim() ?? "";
        var filtered = string.IsNullOrEmpty(query)
            ? source
            : source.Where(e =>
                    e.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                    e.Path.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToList();

        AppList.ItemsSource = filtered;
        ChooseButton.IsEnabled = AppList.SelectedItem is AppEntry;

        if (_loadingInstalled)
        {
            StatusText.Text = "Ищу установленные программы…";
            StatusText.Visibility = Visibility.Visible;
        }
        else if (filtered.Count == 0)
        {
            StatusText.Text = _showRunning
                ? "Запущенных программ не найдено. Укажите .exe вручную."
                : "Ничего не найдено. Укажите .exe вручную.";
            StatusText.Visibility = Visibility.Visible;
        }
        else
        {
            StatusText.Visibility = Visibility.Collapsed;
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    private void RunningTab_Click(object sender, RoutedEventArgs e)
    {
        _showRunning = true;
        RunningTab.IsChecked = true;
        InstalledTab.IsChecked = false;
        ApplyFilter();
    }

    private async void InstalledTab_Click(object sender, RoutedEventArgs e)
    {
        _showRunning = false;
        RunningTab.IsChecked = false;
        InstalledTab.IsChecked = true;

        if (_installed is null)
        {
            _loadingInstalled = true;
            ApplyFilter();
            try
            {
                _installed = await LoadInstalledAsync();
            }
            catch (Exception ex)
            {
                ProfileStore.Log("Каталог установленных программ: " + ex.Message);
                _installed = new List<AppEntry>();
            }
            finally
            {
                _loadingInstalled = false;
            }
        }

        ApplyFilter();
    }

    /// <summary>
    /// Start Menu shortcuts are resolved through WScript.Shell, which wants an
    /// STA thread. Building icons there is fine because they are frozen.
    /// </summary>
    private static Task<List<AppEntry>> LoadInstalledAsync()
    {
        var completion = new TaskCompletionSource<List<AppEntry>>();
        var thread = new Thread(() =>
        {
            try
            {
                completion.SetResult(AppCatalog.Installed());
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        })
        {
            IsBackground = true,
            Name = "MouseMackros app catalog"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private void AppList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        ChooseButton.IsEnabled = AppList.SelectedItem is AppEntry;

    private void AppList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (AppList.SelectedItem is AppEntry) Choose();
    }

    private void Choose_Click(object sender, RoutedEventArgs e) => Choose();

    private void Choose()
    {
        if (AppList.SelectedItem is not AppEntry entry) return;
        Result = entry;
        DialogResult = true;
        Close();
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Выберите программу",
            Filter = "Программы (*.exe)|*.exe|Все файлы (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true) return;

        var path = dialog.FileName;
        string name;
        try
        {
            name = FileVersionInfo.GetVersionInfo(path).FileDescription ?? Path.GetFileNameWithoutExtension(path);
        }
        catch
        {
            name = Path.GetFileNameWithoutExtension(path);
        }

        Result = new AppEntry
        {
            Name = name,
            Path = path,
            Subtitle = path,
            ProcessName = Path.GetFileNameWithoutExtension(path),
            Icon = AppCatalog.IconFor(path)
        };
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
