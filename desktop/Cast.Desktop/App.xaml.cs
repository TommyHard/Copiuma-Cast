using Cast.Desktop.Services;
using Cast.Desktop.ViewModels;
using Cast.Desktop.Views;
using System.Windows;
using System.Windows.Input;

namespace Cast.Desktop;

public partial class App : Application
{
    private LogsWindow? _logsWindow;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        Services.AppLog.Install();
        Services.AppLog.Info("Cast.Desktop запущен.");

        // Глобальный перехват исключений UI
        DispatcherUnhandledException += (_, args) =>
        {
            Services.AppLog.Error($"Необработанное исключение UI: {args.Exception}");
            args.Handled = true;
        };
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Services.AppLog.Error($"Необработанное исключение задачи: {args.Exception}");
            args.SetObserved();
        };

        // Shift+~ — окно логов (работает в любом окне)
        EventManager.RegisterClassHandler(typeof(Window), Window.PreviewKeyDownEvent,
            new KeyEventHandler(OnGlobalKeyDown), handledEventsToo: true);

        // TaskCompletionSource для синхронизации двух потоков
        var splashStartedTcs = new TaskCompletionSource<bool>();
        var splashClosedTcs = new TaskCompletionSource<bool>();

        Thread splashThread = new Thread(() =>
        {
            var splash = new Views.SplashWindow();

            splash.Closed += (s, ev) =>
            {
                splashClosedTcs.TrySetResult(splash.UserClosed);
                System.Windows.Threading.Dispatcher.ExitAllFrames();
            };

            splash.Show();
            splashStartedTcs.TrySetResult(true);

            System.Windows.Threading.Dispatcher.Run();
        });

        splashThread.SetApartmentState(ApartmentState.STA);
        splashThread.IsBackground = true;
        splashThread.Start();

        await splashStartedTcs.Task;

        var options = new Services.DesktopOptions();
        var mainVm = new ViewModels.MainViewModel(options);

        var mainWindow = new MainWindow { DataContext = mainVm };

        var userClosed = await splashClosedTcs.Task;

        if (userClosed)
        {
            Shutdown();
            return;
        }

        Application.Current.MainWindow = mainWindow;
        mainWindow.Show();
    }

    private void OnGlobalKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.OemTilde && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            ToggleLogsWindow();
            e.Handled = true;
        }
    }

    private void ToggleLogsWindow()
    {
        try
        {
            if (_logsWindow is { IsLoaded: true })
            {
                _logsWindow.Activate();
                return;
            }
            _logsWindow = new LogsWindow();

            if (MainWindow is { IsLoaded: true } main && !ReferenceEquals(main, _logsWindow))
                _logsWindow.Owner = main;

            _logsWindow.Closed += (_, _) => _logsWindow = null;
            _logsWindow.Show();
        }
        catch (Exception ex)
        {
            Services.AppLog.Error($"Не удалось открыть окно логов: {ex.Message}");
            _logsWindow = null;
        }
    }
}