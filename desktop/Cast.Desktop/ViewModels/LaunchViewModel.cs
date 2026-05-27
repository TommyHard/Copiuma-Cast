using Cast.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.IO;

namespace Cast.Desktop.ViewModels;

/// <summary>
/// VM запуска игры: выбор exe и манифеста, запуск через GameLauncher с
/// захватом PID и инъекцией оверлея
/// </summary>
public sealed partial class LaunchViewModel : ObservableObject
{
    private readonly DesktopOptions _options;
    private readonly GameBridgeService _bridge;
    private readonly GameLauncher _launcher;

    [ObservableProperty] private string _gameExePath = string.Empty;
    [ObservableProperty] private string _manifestPath = string.Empty;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private string _statusText = "Игра не запущена";
    [ObservableProperty] private string _logText = string.Empty;

    public LaunchViewModel(DesktopOptions options, GameBridgeService bridge)
    {
        _options = options;
        _bridge = bridge;
        _launcher = new GameLauncher(options);
        _launcher.Log += AppendLog;
        _bridge.Log += AppendLog;
    }

    [RelayCommand]
    private void BrowseExe()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Выберите исполняемый файл игры",
            Filter = "Executable (*.exe)|*.exe|All (*.*)|*.*"
        };
        if (dlg.ShowDialog() == true)
            GameExePath = dlg.FileName;
    }

    [RelayCommand]
    private void BrowseManifest()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Выберите манифест мода",
            Filter = "JSON (*.json)|*.json|All (*.*)|*.*"
        };
        if (dlg.ShowDialog() == true)
            ManifestPath = dlg.FileName;
    }

    [RelayCommand]
    private async Task LaunchAsync()
    {
        if (string.IsNullOrWhiteSpace(GameExePath))
        {
            StatusText = "Укажите путь к exe игры.";
            return;
        }

        try
        {
            // Загружаем манифест в GameBridge
            if (!string.IsNullOrWhiteSpace(ManifestPath) && File.Exists(ManifestPath))
                await _bridge.LoadAsync(ManifestPath);

            // Запускаем игру с инъекцией
            await _launcher.LaunchAsync(GameExePath);
            IsRunning = true;
            StatusText = $"Игра запущена (PID: {_launcher.Game?.Id})";

            // Мониторим завершение процесса
            _ = Task.Run(async () =>
            {
                if (_launcher.Game is not null)
                {
                    await _launcher.Game.WaitForExitAsync();
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        IsRunning = false;
                        StatusText = "Игра завершена.";
                        AppendLog("Процесс игры завершён.");
                    });
                }
            });
        }
        catch (Exception ex)
        {
            StatusText = $"Ошибка запуска: {ex.Message}";
            AppendLog($"Ошибка: {ex.Message}");
        }
    }

    [RelayCommand]
    private void Stop()
    {
        try
        {
            _launcher.Game?.Kill();
            _launcher.OverlayHost?.Kill();
            IsRunning = false;
            StatusText = "Игра остановлена.";
        }
        catch (Exception ex)
        {
            AppendLog($"Ошибка остановки: {ex.Message}");
        }
    }

    private void AppendLog(string message)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
            LogText += $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
    }
}