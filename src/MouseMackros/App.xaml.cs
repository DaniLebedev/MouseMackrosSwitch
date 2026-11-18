using System.Windows;
using System.Windows.Threading;
using MouseMackros.Engine;
using MouseMackros.Model;
using MouseMackros.Services;

namespace MouseMackros;

public partial class App : Application
{
    private Mutex? _mutex;
    private EventWaitHandle? _activation;
    private TrayService? _tray;
    private MainWindow? _main;

    public static new App Current => (App)Application.Current;

    public AppConfig Config { get; private set; } = null!;
    public ProfileEngine Engine { get; private set; } = null!;
    public bool SilentStart { get; private set; }
    public bool Exiting { get; private set; }

    private bool _hideHintShown;

    public void TrayHint()
    {
        if (_hideHintShown) return;
        _hideHintShown = true;
        _tray?.ShowInfo("MouseMackros свернулся в трей",
            "Профили продолжают работать. Двойной клик по значку вернёт окно.");
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        SilentStart = e.Args.Any(a => a.Equals("--silent", StringComparison.OrdinalIgnoreCase));

        _mutex = new Mutex(true, @"Local\MouseMackros_SingleInstance", out bool isFirst);
        if (!isFirst)
        {
            try
            {
                if (EventWaitHandle.TryOpenExisting(@"Local\MouseMackros_Activate", out var handle))
                {
                    handle.Set();
                    handle.Dispose();
                }
            }
            catch
            {
                // ignore
            }
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnUnhandled;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            ConfigService.Log("Фатальная ошибка: " + args.ExceptionObject);

        Config = ConfigService.Load();

        try
        {
            Engine = new ProfileEngine(Config);
            Engine.Start();
            Engine.Notice += OnEngineNotice;
            Engine.Error += (_, message) => ConfigService.Log("Ошибка действия: " + message);
        }
        catch (Exception ex)
        {
            ConfigService.Log("Не удалось запустить движок: " + ex);
            AppDialog.ShowError("Не удалось установить глобальные хуки ввода.\n\n" + ex.Message,
                "MouseMackros");
            Shutdown();
            return;
        }

        _tray = new TrayService(this);
        _tray.ProfileChanged(Engine.ActiveProfile);

        Engine.ActiveProfileChanged += (_, _) =>
        {
            _tray?.ProfileChanged(Engine.ActiveProfile);
            _main?.OnEngineProfileChanged(_main, EventArgs.Empty);
        };

        StartupService.Apply(Config.StartWithWindows);

        _activation = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\MouseMackros_Activate");
        var thread = new Thread(() =>
        {
            while (true)
            {
                try
                {
                    if (!_activation.WaitOne()) continue;
                    Dispatcher.BeginInvoke(ShowMainWindow);
                }
                catch
                {
                    return;
                }
            }
        })
        {
            IsBackground = true,
            Name = "MouseMackros activation listener"
        };
        thread.Start();

        if (SilentStart)
        {
            _tray.ShowInfo("MouseMackros запущен",
                "Профили переключаются автоматически. Нажмите Ctrl+Alt+P для паузы.");
        }
        else
        {
            ShowMainWindow();
        }
    }

    private void OnEngineNotice(object? sender, string message)
    {
        if (message.StartsWith("profile:", StringComparison.Ordinal))
        {
            Engine.SwitchProfile(message[8..]);
            return;
        }

        if (Config.ShowToast)
        {
            _tray?.ShowInfo("MouseMackros", message);
        }
    }

    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        ConfigService.Log("Необработанная ошибка: " + e.Exception);
        e.Handled = true;
        AppDialog.ShowError(e.Exception.Message, "Что-то пошло не так");
    }

    public void ShowMainWindow()
    {
        if (_main is null)
        {
            _main = new MainWindow(this);
            _main.Closed += (_, _) => _main = null;
        }

        if (!_main.IsVisible) _main.Show();
        if (_main.WindowState == WindowState.Minimized) _main.WindowState = WindowState.Normal;
        _main.Activate();
    }

    public void ReloadConfig(AppConfig config)
    {
        Config = config;
        Engine.ApplyConfig(config);
        ConfigService.Save(config);
        _main?.ReloadFromConfig();
    }

    public void SaveConfig()
    {
        ConfigService.Save(Config);
    }

    public void ExitApp()
    {
        Exiting = true;
        Engine?.Dispose();
        _tray?.Dispose();
        _activation?.Dispose();
        ConfigService.Save(Config);
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _tray?.Dispose();
            Engine?.Dispose();
            _mutex?.ReleaseMutex();
            _mutex?.Dispose();
        }
        catch
        {
            // ignore
        }
        base.OnExit(e);
    }
}
