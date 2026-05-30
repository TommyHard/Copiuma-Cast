using Cast.Desktop.Models;
using Cast.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace Cast.Desktop.ViewModels;

/// <summary>
/// VM менеджера модов: список доступных модов, установка/удаление/проверка.
/// Гранулярная установка: только библиотеки, только мод или всё
/// </summary>
public sealed partial class ModManagerViewModel : ObservableObject
{
    private readonly ModManagerService _modManager;
    private readonly CastApiClient _api;

    [ObservableProperty] private ObservableCollection<ModEntryViewModel> _availableMods = new();
    [ObservableProperty] private ModEntryViewModel? _selectedMod;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private double _progress;

    public ModManagerViewModel(ModManagerService modManager, CastApiClient api)
    {
        _modManager = modManager;
        _api = api;
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsBusy = true;
        StatusText = "Загрузка списка модов...";
        try
        {
            var packages = await _api.GetAvailableModsAsync();
            AvailableMods.Clear();
            foreach (var p in packages)
            {
                var installed = _modManager.GetInstalled(p.GameId);
                AvailableMods.Add(new ModEntryViewModel
                {
                    GameId = p.GameId,
                    GameName = p.GameName,
                    ServerVersion = p.ModVersion,
                    InstalledVersion = installed?.ModVersion,
                    IsInstalled = installed is not null,
                    GameDirectory = installed?.GameDirectory ?? string.Empty,
                    Package = p
                });
            }
            StatusText = $"Доступно модов: {packages.Count}";
        }
        catch (Exception ex)
        {
            AppLog.Info($"Ошибка: {ex.Message}");
            StatusText = $"Ошибка: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Параметр — строка из XAML CommandParameter ("All", "LibrariesOnly", "ModOnly")
    /// </summary>
    [RelayCommand]
    private async Task InstallAsync(string? modeStr)
    {
        if (SelectedMod is null || string.IsNullOrWhiteSpace(SelectedMod.GameDirectory))
        {
            StatusText = "Выберите мод и укажите директорию игры.";
            return;
        }

        if (!Enum.TryParse<InstallMode>(modeStr, true, out var mode))
            mode = InstallMode.All;

        IsBusy = true;
        try
        {
            var reporter = new Progress<(int current, int total, string file)>(p =>
            {
                Progress = (double)p.current / p.total * 100;
                StatusText = $"[{p.current}/{p.total}] {p.file}";
            });

            await _modManager.InstallAsync(SelectedMod.Package, SelectedMod.GameDirectory, mode, reporter);
            SelectedMod.IsInstalled = true;
            SelectedMod.InstalledVersion = SelectedMod.ServerVersion;
            StatusText = "Установка завершена.";
        }
        catch (Exception ex)
        {
            AppLog.Info($"Ошибка: {ex.Message}");
            StatusText = $"Ошибка установки: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            Progress = 0;
        }
    }

    [RelayCommand]
    private async Task UninstallAsync()
    {
        if (SelectedMod is null) return;
        IsBusy = true;
        try
        {
            await _modManager.UninstallAsync(SelectedMod.GameId);
            SelectedMod.IsInstalled = false;
            SelectedMod.InstalledVersion = null;
            StatusText = "Мод удалён.";
        }
        catch (Exception ex)
        {
            AppLog.Info($"Ошибка: {ex.Message}");
            StatusText = $"Ошибка удаления: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task VerifyAsync()
    {
        if (SelectedMod is null) return;
        IsBusy = true;
        try
        {
            var report = await _modManager.VerifyAsync(SelectedMod.GameId);
            StatusText = report.IsValid
                ? "Целостность в порядке."
                : $"Нарушения: {string.Join("; ", report.Issues)}";
        }
        catch (Exception ex)
        {
            AppLog.Info($"Ошибка: {ex.Message}");
            StatusText = $"Ошибка проверки: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}

/// <summary>
/// VM одной записи мода в списке
/// </summary>
public sealed partial class ModEntryViewModel : ObservableObject
{
    [ObservableProperty] private string _gameId = string.Empty;
    [ObservableProperty] private string _gameName = string.Empty;
    [ObservableProperty] private string _serverVersion = string.Empty;
    [ObservableProperty] private string? _installedVersion;
    [ObservableProperty] private bool _isInstalled;
    [ObservableProperty] private string _gameDirectory = string.Empty;

    /// <summary>
    /// Серверный пакет (для передачи в ModManagerService)
    /// </summary>
    public ModPackageInfo Package { get; set; } = new();
}