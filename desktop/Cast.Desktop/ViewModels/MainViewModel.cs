using Cast.Desktop.Localization;
using Cast.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Windows;

namespace Cast.Desktop.ViewModels;

/// <summary>
/// Корневая VM: навигация между страницами, состояние авторизации
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly DesktopOptions _options;
    private readonly DesktopAuthService _auth;

    private ModManagerViewModel? _mods;
    private RoomViewModel? _room;
    private LaunchViewModel? _launch;
    private DebugViewModel? _debug;
    private SettingsViewModel? _settings;

    [ObservableProperty] private bool _isLoggedIn;
    [ObservableProperty] private string _username = string.Empty;
    [ObservableProperty] private object? _currentPage;
    [ObservableProperty] private string _selectedNav = "mods";

    // Сервисы, доступные после логина
    private CastApiClient? _api;
    private GameBridgeService? _bridge;
    private RoomHubClient? _hub;
    private PresenceHubClient? _presence;

    public MainViewModel(DesktopOptions options)
    {
        _options = options;
        _auth = new DesktopAuthService(options);
    }

    [RelayCommand]
    private async Task LoginAsync()
    {
        try
        {
            var token = await _auth.LoginViaBrowserAsync();

            // Десктоп — инструмент стримера: пускаем только стримеров и админов
            if (!_auth.IsStreamerOrAdmin())
            {
                AppLog.Info("Вход отклонён: недостаточно прав (нужен Streamer/Admin).");
                MessageBox.Show("Cast.Desktop доступен только стримерам и администраторам.",
                    "Доступ запрещён", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _api = new CastApiClient(_options, token);
            _bridge = new GameBridgeService();

            var modManager = new ModManagerService(_api);
            _mods = new ModManagerViewModel(modManager, _api);
            _room = new RoomViewModel(_api, _options, _bridge)
            {
                HubConnector = (roomId, code, ct) => ConnectHubAsync(roomId, code, ct),
                KickConnector = (roomId, targetId, ban) => _hub is not null ? _hub.KickAsync(roomId, targetId, ban) : Task.CompletedTask
            };
            _launch = new LaunchViewModel(_options, _bridge,
                roomCodeProvider: () => _room?.HasRoom == true ? _room.RoomCode : null,
                tokenProvider: () => _auth.Token,
                manifestResolver: () => ResolveManifestPathAsync(modManager));
            _debug = new DebugViewModel(_bridge);

            // Настройки (горячая клавиша оверлея) — на аккаунт; сразу применяем
            // сохранённую клавишу к конфигу оверлея
            var desktopSettings = new DesktopSettings();
            var userId = _auth.GetUserId();
            desktopSettings.ApplyOverlayHotkey(userId);
            _settings = new SettingsViewModel(desktopSettings, userId);

            // Платформенное присутствие: держим соединение, чтобы бэкенд считал
            // стримера онлайн и показывал статус "Играет в ..."
            _presence = new PresenceHubClient(_options, () => _auth.Token!);
            try { await _presence.ConnectAsync(); }
            catch (Exception ex) { AppLog.Error($"Presence-хаб недоступен: {ex.Message}"); }

            IsLoggedIn = true;
            Username = "Streamer"; // Имя приходит из токена; пока заглушка
            AppLog.Info("Авторизация выполнена.");
            Navigate("mods");
        }
        catch (Exception ex)
        {
            AppLog.Error($"Ошибка авторизации: {ex.Message}");
            MessageBox.Show(ex.Message, "Ошибка авторизации", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task LogoutAsync()
    {
        AppLog.Info("Выход из аккаунта.");
        IsLoggedIn = false;
        CurrentPage = null;
        _api = null;
        if (_bridge is not null)
            await _bridge.DisposeAsync();
        _bridge = null;
        if (_hub is not null)
            await _hub.DisposeAsync();
        _hub = null;
        if (_presence is not null)
            await _presence.DisposeAsync();
        _presence = null;
    }

    [RelayCommand]
    private void Navigate(string page)
    {
        AppLog.Info($"Навигация: {page}");
        SelectedNav = page;
        CurrentPage = page switch
        {
            "mods" => _mods,
            "room" => _room,
            "launch" => _launch,
            "debug" => _debug,
            _ => _mods
        };
    }

    [RelayCommand]
    private void SwitchLanguage(string lang)
    {
        LocalizationManager.SetLanguage(lang);
    }

    /// <summary>
    /// Авто-поиск манифеста установленного мода для игры текущей комнаты.
    /// Берёт директорию установки из состояния менеджера модов и относительный
    /// путь манифеста из пакета мода с сервера. null — если определить нельзя
    /// </summary>
    private async Task<string?> ResolveManifestPathAsync(ModManagerService modManager)
    {
        var gameId = _room?.GameId;
        if (_api is null || string.IsNullOrEmpty(gameId)) return null;

        var installed = modManager.GetInstalled(gameId);
        if (installed is null) return null;

        try
        {
            var pkg = await _api.GetModPackageAsync(gameId);
            if (pkg?.Manifest is null) return null;
            var path = System.IO.Path.Combine(installed.GameDirectory, pkg.Manifest.RelativePath);
            return System.IO.File.Exists(path) ? path : null;
        }
        catch { return null; }
    }

    /// <summary>
    /// Подключить SignalR к комнате после её создания (вызывается из RoomViewModel)
    /// </summary>
    internal async Task ConnectHubAsync(Guid roomId, string roomCode, CancellationToken ct = default)
    {
        if (_api is null || _bridge is null || _auth.Token is null) return;

        if (_hub is not null)
            await _hub.DisposeAsync();
        _hub = new RoomHubClient(_options, () => _auth.Token!, _bridge);
        _hub.Log += msg => _room?.AppendLog(msg);
        _hub.RosterChanged += roster => _room?.UpdateRoster(roster);
        _hub.BetUpdated += () => _room?.OnBetUpdated();
        _hub.MediaVolumeChanged += v => _room?.ApplyServerVolume(v);
        if (_room is not null)
            _room.VolumeConnector = vol => _hub?.SetMediaVolumeAsync(vol) ?? Task.CompletedTask;
        await _hub.ConnectAsync(roomId, roomCode, ct);
    }
}