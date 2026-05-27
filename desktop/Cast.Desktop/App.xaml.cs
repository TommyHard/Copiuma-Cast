using Cast.Desktop.Services;
using Cast.Desktop.ViewModels;
using System.Windows;

namespace Cast.Desktop;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var options = new DesktopOptions();
        var mainVm = new MainViewModel(options);

        var mainWindow = new MainWindow { DataContext = mainVm };
        mainWindow.Show();
    }
}