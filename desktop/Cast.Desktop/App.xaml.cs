using Cast.Desktop.Services;
using Cast.Desktop.ViewModels;
using Cast.Desktop.Views;
using System.Windows;
using System.Windows.Input;

namespace Cast.Desktop;

public partial class App : Application
{
    private LogsWindow? _logsWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        AppLog.Install();
        AppLog.Info("Cast.Desktop запущен.");

        // Shift+~ (тильда) — окно полных логов программы (работает в любом окне)
        EventManager.RegisterClassHandler(typeof(Window), Window.PreviewKeyDownEvent,
            new KeyEventHandler(OnGlobalKeyDown), handledEventsToo: true);

        var options = new DesktopOptions();
        var mainVm = new MainViewModel(options);

        var mainWindow = new MainWindow { DataContext = mainVm };
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
        if (_logsWindow is { IsLoaded: true })
        {
            _logsWindow.Activate();
            return;
        }
        _logsWindow = new LogsWindow { Owner = MainWindow };
        _logsWindow.Closed += (_, _) => _logsWindow = null;
        _logsWindow.Show();
    }
}