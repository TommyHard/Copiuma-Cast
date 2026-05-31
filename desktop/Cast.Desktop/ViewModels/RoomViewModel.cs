using Cast.Desktop.Services;
using Cast.Shared.GameBridge;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.Linq;
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
    internal Func<Guid, string, CancellationToken, Task>? HubConnector { get; set; }

    /// <summary>
    /// Колбэк кика/бана зрителя (устанавливается MainViewModel)
    /// </summary>
    internal Func<Guid, Guid, bool, Task>? KickConnector { get; set; }

    /// <summary>
    /// Доступные игры для выбора при создании комнаты
    /// </summary>
    [ObservableProperty] private ObservableCollection<CastApiClient.GameInfo> _availableGames = new();

    /// <summary>
    /// События игры комнаты с признаком включения (стример управляет доступностью)
    /// </summary>
    [ObservableProperty] private ObservableCollection<CastApiClient.RoomEvent> _events = new();

    /// <summary>
    /// Ставки комнаты (создаёт/разрешает/отменяет стример)
    /// </summary>
    [ObservableProperty] private ObservableCollection<BetVm> _bets = new();

    // Поля формы создания ставки
    [ObservableProperty] private string _newBetTitle = string.Empty;
    [ObservableProperty] private string _newBetOutcomes = string.Empty;
    [ObservableProperty] private int _newBetDuration = 60;

    public RoomViewModel(CastApiClient api, DesktopOptions options, GameBridgeService bridge)
    {
        _api = api;
        _options = options;
        _bridge = bridge;

        // Раз в секунду: обновляем обратный отсчёт открытых ставок и убираем
        // закрытые старше 30 c, чтобы не копились
        _betCleanup = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _betCleanup.Tick += (_, _) =>
        {
            try
            {
                var cutoff = DateTimeOffset.UtcNow - TimeSpan.FromSeconds(30);
                for (var i = Bets.Count - 1; i >= 0; i--)
                {
                    if (!Bets[i].IsOpen && Bets[i].ResolvedAt is { } r && r < cutoff)
                        Bets.RemoveAt(i);
                    else
                        Bets[i].Tick();
                }
            }
            catch (Exception ex) { AppLog.Error($"betCleanup: {ex.Message}"); }
        };
        _betCleanup.Start();
    }

    private readonly System.Windows.Threading.DispatcherTimer _betCleanup;

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
                await HubConnector(room.Id, room.Code, CancellationToken.None);

            // Подгружаем события игры и ставки комнаты для панелей управления
            await LoadEventsAsync();
            await LoadBetsAsync();
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

    /// <summary>
    /// Сумма выдачи монет зрителю (валюта локальна для стримера)
    /// </summary>
    [ObservableProperty] private long _grantAmount = 100;

    /// <summary>
    /// Громкость медиа (0-100). Регулятор управляет громкостью в оверлее;
    /// значение синхронизируется через хаб
    /// </summary>
    [ObservableProperty] private int _mediaVolume = 100;

    private bool _volumeEcho;

    /// <summary>
    /// Колбэк отправки громкости на сервер (ставит MainViewModel)
    /// </summary>
    internal Func<int, Task>? VolumeConnector { get; set; }

    partial void OnMediaVolumeChanged(int value)
    {
        if (_volumeEcho) return;
        _ = VolumeConnector?.Invoke(value);
    }

    /// <summary>
    /// Применить громкость, пришедшую с сервера, без обратной отправки
    /// </summary>
    internal void ApplyServerVolume(int volume)
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            _volumeEcho = true;
            MediaVolume = Math.Clamp(volume, 0, 100);
            _volumeEcho = false;
        });
    }

    [RelayCommand]
    private async Task Grant(RoomMember? member)
    {
        if (member is null || !HasRoom) return;
        if (!Guid.TryParse(member.UserId, out var targetId)) return;
        if (string.Equals(member.Role, "Streamer", StringComparison.OrdinalIgnoreCase)) return;
        if (GrantAmount <= 0) { StatusText = "Сумма должна быть положительной."; return; }
        try
        {
            await _api.GrantCoinsAsync(RoomId, targetId, GrantAmount);
            StatusText = $"Выдано {GrantAmount} зрителю {member.DisplayName}.";
            AppendLog(StatusText);
        }
        catch (Exception ex)
        {
            AppLog.Info($"Ошибка: {ex.Message}");
            AppendLog($"Ошибка выдачи валюты: {ex.Message}");
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

    // --- События игры (включение/выключение стримером) ---

    private async Task LoadEventsAsync()
    {
        if (!HasRoom) return;
        try
        {
            var list = await _api.GetRoomEventsAsync(RoomId);
            Events.Clear();
            foreach (var e in list) Events.Add(e);
        }
        catch (Exception ex)
        {
            AppLog.Info($"Ошибка: {ex.Message}");
            AppendLog($"Ошибка загрузки событий: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task ToggleEvent(CastApiClient.RoomEvent? ev)
    {
        if (ev is null || !HasRoom) return;
        try
        {
            await _api.SetEventEnabledAsync(RoomId, ev.EventId, !ev.Enabled);
            await LoadEventsAsync();
        }
        catch (Exception ex)
        {
            AppLog.Info($"Ошибка: {ex.Message}");
            AppendLog($"Ошибка переключения события: {ex.Message}");
        }
    }

    // --- Ставки ---

    private async Task LoadBetsAsync()
    {
        if (!HasRoom) return;
        try
        {
            var list = await _api.GetBetsAsync(RoomId);
            Bets.Clear();
            foreach (var b in list) Bets.Add(new BetVm(b));
        }
        catch (Exception ex)
        {
            AppLog.Info($"Ошибка: {ex.Message}");
            AppendLog($"Ошибка загрузки ставок: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task CreateBetAsync()
    {
        if (!HasRoom) return;
        var outcomes = NewBetOutcomes
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        if (string.IsNullOrWhiteSpace(NewBetTitle) || outcomes.Count < 2)
        {
            StatusText = "Нужны вопрос и минимум два исхода (через запятую).";
            return;
        }
        try
        {
            await _api.CreateBetAsync(RoomId, NewBetTitle.Trim(), outcomes, NewBetDuration);
            NewBetTitle = string.Empty;
            NewBetOutcomes = string.Empty;
            await LoadBetsAsync();
            AppendLog("Ставка создана.");
        }
        catch (Exception ex)
        {
            AppLog.Info($"Ошибка: {ex.Message}");
            AppendLog($"Ошибка создания ставки: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task ResolveBet(BetOutcomeVm? outcome)
    {
        if (outcome is null || !HasRoom) return;
        try
        {
            await _api.ResolveBetAsync(RoomId, outcome.BetId, outcome.OutcomeId);
            await LoadBetsAsync();
            AppendLog($"Ставка разрешена: {outcome.Label}.");
        }
        catch (Exception ex)
        {
            AppLog.Info($"Ошибка: {ex.Message}");
            AppendLog($"Ошибка разрешения ставки: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task CancelBet(BetVm? bet)
    {
        if (bet is null || !HasRoom) return;
        try
        {
            await _api.CancelBetAsync(RoomId, bet.Id);
            await LoadBetsAsync();
            AppendLog("Ставка отменена.");
        }
        catch (Exception ex)
        {
            AppLog.Info($"Ошибка: {ex.Message}");
            AppendLog($"Ошибка отмены ставки: {ex.Message}");
        }
    }

    /// <summary>
    /// Сервер сообщил об изменении ставки (через хаб) — перечитываем список.
    /// Вызывается из фонового потока SignalR, поэтому маршалим в UI-поток
    /// </summary>
    internal void OnBetUpdated()
        => Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            try { _ = LoadBetsAsync(); }
            catch (Exception ex) { AppLog.Error($"OnBetUpdated: {ex.Message}"); }
        });

    internal void AppendLog(string message)
    {
        AppLog.Info($"[room] {message}");
        // BeginInvoke: не блокируем и не пробрасываем исключение
        // обратно в фоновый поток SignalR, чтобы один сбой не дестабилизировал UI
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            try { Log.Insert(0, $"[{DateTime.Now:HH:mm:ss}] {message}"); }
            catch (Exception ex) { AppLog.Error($"AppendLog: {ex.Message}"); }
        });
    }

    /// <summary>
    /// Применить пришедший с сервера ростер: список участников и счётчик онлайна.
    /// Маршалим в UI-поток через BeginInvoke и защищаемся try/catch — обработчик
    /// хаба никогда не должен ронять приложение или ломать навигацию
    /// </summary>
    internal void UpdateRoster(RoomRoster roster)
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            try
            {
                Members.Clear();
                foreach (var m in roster?.Members ?? new())
                    Members.Add(m);
                OnlineCount = roster?.Online ?? 0;
            }
            catch (Exception ex) { AppLog.Error($"UpdateRoster: {ex.Message}"); }
        });
    }
}

/// <summary>
/// Презентационная обёртка ставки для списка в UI стримера. ObservableObject,
/// чтобы обратный отсчёт обновлялся в реальном времени
/// </summary>
public sealed partial class BetVm : ObservableObject
{
    public Guid Id { get; }
    public string Title { get; }
    public string Status { get; }
    public bool IsOpen { get; }
    public long TotalPool { get; }
    public DateTimeOffset LocksAt { get; }
    public ObservableCollection<BetOutcomeVm> Outcomes { get; } = new();

    /// <summary>
    /// Обратный отсчёт до закрытия приёма (обновляется таймером)
    /// </summary>
    [ObservableProperty] private string _closesAtText = string.Empty;

    /// <summary>
    /// Когда ставка закрыта (для автоудаления через 30 c)
    /// </summary>
    public DateTimeOffset? ResolvedAt { get; }

    /// <summary>
    /// Итог после разрешения: крупнейший выигрыш и общая выплата
    /// </summary>
    public string ResultText { get; }

    public BetVm(CastApiClient.Bet bet)
    {
        Id = bet.Id;
        Title = bet.Title;
        Status = bet.Status;
        IsOpen = string.Equals(bet.Status, "Open", StringComparison.OrdinalIgnoreCase);
        TotalPool = bet.TotalPool;
        LocksAt = bet.LocksAt;
        ResolvedAt = bet.ResolvedAt;
        ResultText = IsOpen
            ? string.Empty
            : bet.TopWinnerName is not null
                ? $"Победитель: {bet.TopWinnerName} (+{bet.TopWinnerPayout}) · выплачено {bet.PaidOut}"
                : string.Equals(bet.Status, "Cancelled", StringComparison.OrdinalIgnoreCase)
                    ? "Отменено, ставки возвращены"
                    : "Никто не угадал — возврат";
        foreach (var o in bet.Outcomes)
            Outcomes.Add(new BetOutcomeVm(bet.Id, o, IsOpen, o.Id == bet.WinningOutcomeId));
        Tick();
    }

    /// <summary>
    /// Пересчитать обратный отсчёт. Пока приём открыт — "приём: N с"; по
    /// истечении — "приём закрыт" (ставка ещё Open, ждёт разрешения стримером)
    /// </summary>
    public void Tick()
    {
        if (!IsOpen) { ClosesAtText = string.Empty; return; }
        var secs = (int)Math.Ceiling((LocksAt - DateTimeOffset.UtcNow).TotalSeconds);
        ClosesAtText = secs > 0 ? $"приём: {secs} с" : "приём закрыт";
    }
}

/// <summary>
/// Исход ставки; несёт BetId, чтобы команда разрешения знала, какую ставку
/// закрывать
/// </summary>
public sealed class BetOutcomeVm
{
    public Guid BetId { get; }
    public Guid OutcomeId { get; }
    public string Label { get; }
    public long Pool { get; }
    public decimal? Odds { get; }
    /// <summary>
    /// Можно ли разрешить ставку этим исходом (ставка открыта)
    /// </summary>
    public bool CanResolve { get; }
    /// <summary>
    /// Этот исход — победивший (для подсветки)
    /// </summary>
    public bool IsWinner { get; }

    public BetOutcomeVm(Guid betId, CastApiClient.BetOutcome o, bool canResolve, bool isWinner)
    {
        BetId = betId;
        OutcomeId = o.Id;
        Label = o.Label;
        Pool = o.Pool;
        Odds = o.Odds;
        CanResolve = canResolve;
        IsWinner = isWinner;
    }
}