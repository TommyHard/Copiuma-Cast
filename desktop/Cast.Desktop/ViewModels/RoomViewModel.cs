using Cast.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Windows;

namespace Cast.Desktop.ViewModels;

/// <summary>
/// VM управления комнатой: создание сессии, лимит зрителей, коды/ссылки,
/// инвайты по @identifier
/// </summary>
public sealed partial class RoomViewModel : ObservableObject
{
    private readonly CastApiClient _api;
    private readonly DesktopOptions _options;
    private readonly GameBridgeService _bridge;

    [ObservableProperty] private string _roomTitle = string.Empty;
    [ObservableProperty] private string? _gameId;
    [ObservableProperty] private int _viewerLimit;
    [ObservableProperty] private bool _hasRoom;
    [ObservableProperty] private string _roomCode = string.Empty;
    [ObservableProperty] private Guid _roomId;
    [ObservableProperty] private string _inviteIdentifier = string.Empty;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private int _onlineCount;
    [ObservableProperty] private ObservableCollection<string> _log = new();

    /// <summary>
    /// Доступные игры для выбора при создании комнаты
    /// </summary>
    [ObservableProperty] private ObservableCollection<CastApiClient.GameInfo> _availableGames = new();

    public RoomViewModel(CastApiClient api, DesktopOptions options, GameBridgeService bridge)
    {
        _api = api;
        _options = options;
        _bridge = bridge;
    }

    [RelayCommand]
    private async Task LoadGamesAsync()
    {
        try
        {
            var games = await _api.GetGamesAsync();
            AvailableGames.Clear();
            foreach (var g in games)
                AvailableGames.Add(g);
        }
        catch { /* ignore */ }
    }

    [RelayCommand]
    private async Task CreateRoomAsync()
    {
        if (string.IsNullOrWhiteSpace(RoomTitle))
        {
            StatusText = "Укажите название комнаты.";
            return;
        }

        try
        {
            var room = await _api.CreateRoomAsync(RoomTitle, GameId, ViewerLimit);
            RoomId = room.Id;
            RoomCode = room.Code;
            HasRoom = true;
            StatusText = $"Комната создана: {room.Code}";
            AppendLog($"Комната {room.Code} создана.");
        }
        catch (Exception ex)
        {
            StatusText = $"Ошибка: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task CloseRoomAsync()
    {
        if (!HasRoom) return;
        try
        {
            await _api.CloseRoomAsync(RoomId);
            HasRoom = false;
            StatusText = "Комната закрыта.";
            AppendLog("Комната закрыта.");
        }
        catch (Exception ex)
        {
            StatusText = $"Ошибка: {ex.Message}";
        }
    }

    [RelayCommand]
    private void CopyCode()
    {
        if (!string.IsNullOrEmpty(RoomCode))
            Clipboard.SetText(RoomCode);
    }

    [RelayCommand]
    private async Task CopyLinkAsync()
    {
        if (!HasRoom) return;
        try
        {
            var link = await _api.GetInviteLinkAsync(RoomId);
            Clipboard.SetText(link);
            StatusText = "Ссылка скопирована.";
        }
        catch (Exception ex)
        {
            StatusText = $"Ошибка: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task InviteAsync()
    {
        if (!HasRoom || string.IsNullOrWhiteSpace(InviteIdentifier)) return;
        try
        {
            var id = InviteIdentifier.TrimStart('@');
            await _api.InviteAsync(RoomId, id);
            StatusText = $"Приглашение отправлено: @{id}";
            AppendLog($"Приглашён @{id}.");
            InviteIdentifier = string.Empty;
        }
        catch (Exception ex)
        {
            StatusText = $"Ошибка: {ex.Message}";
        }
    }

    internal void AppendLog(string message)
    {
        Application.Current.Dispatcher.Invoke(() =>
            Log.Insert(0, $"[{DateTime.Now:HH:mm:ss}] {message}"));
    }
}