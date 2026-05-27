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

    [ObservableProperty] private bool _isLoggedIn;
    [ObservableProperty] private string _username = string.Empty;
    [ObservableProperty] private object? _currentPage;
    [ObservableProperty] private string _selectedNav = "mods";

    // Сервисы, доступные после логина
    private CastApiClient? _api;
    private GameBridgeService? _bridge;
    private RoomHubClient? _hub;

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
            _api = new CastApiClient(_options, token);
            _bridge = new GameBridgeService();

            _mods = new ModManagerViewModel(new ModManagerService(_api), _api);
            _room = new RoomViewModel(_api, _options, _bridge);
            _launch = new LaunchViewModel(_options, _bridge);
            _debug = new DebugViewModel(_bridge);

            IsLoggedIn = true;
            Username = "Streamer"; // Имя приходит из токена; пока заглушка
            Navigate("mods");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Ошибка авторизации", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task LogoutAsync()
    {
        IsLoggedIn = false;
        CurrentPage = null;
        _api = null;
        if (_bridge is not null)
            await _bridge.DisposeAsync();
        _bridge = null;
        if (_hub is not null)
            await _hub.DisposeAsync();
        _hub = null;
    }

    [RelayCommand]
    private void Navigate(string page)
    {
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
    /// Подключить SignalR к комнате после её создания (вызывается из RoomViewModel)
    /// </summary>
    internal async Task ConnectHubAsync(string roomCode, CancellationToken ct = default)
    {
        if (_api is null || _bridge is null || _auth.Token is null) return;

        if (_hub is not null)
            await _hub.DisposeAsync();
        _hub = new RoomHubClient(_options, () => _auth.Token!, _bridge);
        _hub.Log += msg => _room?.AppendLog(msg);
        await _hub.ConnectAsync(roomCode, ct);
    }
}