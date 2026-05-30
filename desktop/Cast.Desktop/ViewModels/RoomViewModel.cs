using Cast.Desktop.Services;
using Cast.Shared.GameBridge;
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
    /// Подключённые к комнате участники (отображаются в списке)
    /// </summary>
    [ObservableProperty] private ObservableCollection<RoomMember> _members = new();

    /// <summary>
    /// Колбэк подключения SignalR к комнате (устанавливается MainViewModel).
    /// Нужен, чтобы после создания комнаты начать принимать ростер и команды
    /// </summary>
    internal Func<string, CancellationToken, Task>? HubConnector { get; set; }

    /// <summary>
    /// Колбэк кика/бана зрителя (устанавливается MainViewModel)
    /// </summary>
    internal Func<Guid, Guid, bool, Task>? KickConnector { get; set; }

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
        catch (Exception ex)
        {
            AppLog.Info($"Ошибка: {ex.Message}");
            AppendLog($"Ошибка загрузки списка игр: {ex.Message}");
        }
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

            // Подключаемся к хабу комнаты: команды зрителей + список участников
            if (HubConnector is not null)
                await HubConnector(room.Code, CancellationToken.None);
        }
        catch (Exception ex)
        {
            AppLog.Info($"Ошибка: {ex.Message}");
            StatusText = $"Ошибка: {ex.Message}";
            AppendLog($"Ошибка создания комнаты: {ex.Message}");
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
            AppLog.Info($"Ошибка: {ex.Message}");
            StatusText = $"Ошибка: {ex.Message}";
            AppendLog($"Ошибка закрытия комнаты: {ex.Message}");
        }
    }

    [RelayCommand]
    private void CopyCode()
    {
        if (!string.IsNullOrEmpty(RoomCode))
        {
            Clipboard.SetText(RoomCode);
            AppendLog("Код комнаты скопирован в буфер обмена.");
        }
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
            AppendLog("Ссылка-приглашение скопирована в буфер обмена.");
        }
        catch (Exception ex)
        {
            AppLog.Info($"Ошибка: {ex.Message}");
            StatusText = $"Ошибка: {ex.Message}";
            AppendLog($"Ошибка копирования ссылки: {ex.Message}");
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
            AppendLog($"Приглашён пользователь @{id}.");
            InviteIdentifier = string.Empty;
        }
        catch (Exception ex)
        {
            AppLog.Info($"Ошибка: {ex.Message}");
            StatusText = $"Ошибка: {ex.Message}";
            AppendLog($"Ошибка отправки приглашения: {ex.Message}");
        }
    }

    [RelayCommand]
    private Task Kick(RoomMember? member) => KickOrBan(member, ban: false);

    [RelayCommand]
    private Task Block(RoomMember? member) => KickOrBan(member, ban: true);

    private async Task KickOrBan(RoomMember? member, bool ban)
    {
        if (member is null || KickConnector is null) return;
        if (!Guid.TryParse(member.UserId, out var targetId)) return;
        if (string.Equals(member.Role, "Streamer", StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            await KickConnector(RoomId, targetId, ban);
            StatusText = ban ? $"Зритель {member.DisplayName} заблокирован." : $"Зритель {member.DisplayName} выгнан.";
            AppendLog(StatusText);
        }
        catch (Exception ex)
        {
            AppLog.Info($"Ошибка: {ex.Message}");
            StatusText = $"Ошибка: {ex.Message}";
            AppendLog($"Ошибка ограничения доступа: {ex.Message}");
        }
    }

    internal void AppendLog(string message)
    {
        Application.Current.Dispatcher.Invoke(() =>
            Log.Insert(0, $"[{DateTime.Now:HH:mm:ss}] {message}"));
        AppLog.Info($"[room] {message}");
    }

    /// <summary>
    /// Применить пришедший с сервера ростер: список участников и счётчик онлайна
    /// </summary>
    internal void UpdateRoster(RoomRoster roster)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            Members.Clear();
            foreach (var m in roster.Members)
                Members.Add(m);
            OnlineCount = roster.Online;
        });
    }
}