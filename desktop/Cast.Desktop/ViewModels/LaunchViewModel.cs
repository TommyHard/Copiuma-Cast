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
    // Поставщики кода активной комнаты и токена — нужны, чтобы оверлей
    // подключился к комнате (ростер/кик-бан). Устанавливаются MainViewModel
    private readonly Func<string?>? _roomCodeProvider;
    private readonly Func<string?>? _tokenProvider;

    [ObservableProperty] private string _gameExePath = string.Empty;
    [ObservableProperty] private string _manifestPath = string.Empty;

    /// <summary>
    /// Аргументы командной строки, передаваемые игре при запуске (напр. -windowed)
    /// </summary>
    [ObservableProperty] private string _launchArguments = string.Empty;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private string _statusText = "Игра не запущена";
    [ObservableProperty] private string _logText = string.Empty;

    // Возвращает путь к манифесту установленного мода для текущей игры комнаты
    // (для авто-загрузки без ручного выбора файла). null — если не определить
    private readonly Func<Task<string?>>? _manifestResolver;

    public LaunchViewModel(DesktopOptions options, GameBridgeService bridge,
        Func<string?>? roomCodeProvider = null, Func<string?>? tokenProvider = null,
        Func<Task<string?>>? manifestResolver = null)
    {
        _options = options;
        _bridge = bridge;
        _roomCodeProvider = roomCodeProvider;
        _tokenProvider = tokenProvider;
        _manifestResolver = manifestResolver;
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
            // Манифест: путь, указанный вручную, иначе — авто-поиск по
            // установленному моду игры комнаты (чтобы не выбирать файл руками)
            var manifestPath = ManifestPath;
            if (string.IsNullOrWhiteSpace(manifestPath) && _manifestResolver is not null)
            {
                var auto = await _manifestResolver();
                if (!string.IsNullOrWhiteSpace(auto))
                {
                    manifestPath = auto;
                    AppendLog($"Манифест найден автоматически: {Path.GetFileName(auto)}");
                }
            }
            if (!string.IsNullOrWhiteSpace(manifestPath) && File.Exists(manifestPath))
                await _bridge.LoadAsync(manifestPath);
            else
                AppendLog("Манифест не задан — события в игре работать не будут, пока мост не подключён.");

            // Запускаем игру с инъекцией; оверлею передаём код комнаты и токен
            await _launcher.LaunchAsync(GameExePath, LaunchArguments,
                _roomCodeProvider?.Invoke(), _tokenProvider?.Invoke());
            IsRunning = true;
            StatusText = $"Игра запущена (PID: {_launcher.Game?.Id})";

            // Мониторим завершение процесса
            _ = Task.Run(async () =>
            {
                if (_launcher.Game is not null)
                {
                    await _launcher.Game.WaitForExitAsync();
                    // Игра закрылась сама — гасим оверлей вместе с CEF-дочерними
                    _launcher.StopOverlay();
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
            _launcher.Stop();
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