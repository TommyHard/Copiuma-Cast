using Cast.API.Bets;
using Cast.API.Common;
using Cast.API.Economy;
using Cast.API.Events;
using Cast.API.Games;
using Cast.API.Media;
using Cast.API.Rooms;
using Cast.Shared.GameBridge;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Cast.API.Realtime;

/// <summary>
/// Реал-тайм-канал комнаты. Зритель входит в комнату и инициирует события;
/// стример (владелец) попадает в отдельную группу, куда хаб доставляет готовые
/// игровые команды НАПРЯМУЮ (без брокера) — это горячий путь, критичный к
/// задержке. Группы:
///   room:{roomId}     — все участники (чат/уведомления),
///   streamer:{roomId} — соединение(я) стримера (доставка команд моду)
/// </summary>
[Authorize]
public sealed class RoomHub : Hub
{
    private readonly RoomService _rooms;
    private readonly ManifestCatalog _catalog;
    private readonly WalletService _wallet;
    private readonly PresenceService _presence;
    private readonly BettingService _betting;
    private readonly MediaService _media;
    private readonly IEventBus _bus;
    private readonly GameService _gameService;

    public RoomHub(RoomService rooms, ManifestCatalog catalog, WalletService wallet,
        PresenceService presence, BettingService betting, MediaService media, IEventBus bus, GameService gameService)
    {
        _rooms = rooms;
        _catalog = catalog;
        _wallet = wallet;
        _presence = presence;
        _betting = betting;
        _media = media;
        _bus = bus;
        _gameService = gameService;
    }

    public static string RoomGroup(Guid roomId) => $"room:{roomId}";
    public static string StreamerGroup(Guid roomId) => $"streamer:{roomId}";

    /// <summary>
    /// Подключиться к комнате по коду. Возвращает информацию о комнате/роли
    /// </summary>
    public async Task<RoomDto> JoinRoom(string code)
    {
        var userId = Context.User?.GetUserId()
            ?? throw new HubException("Не авторизован.");

        var resolved = await _rooms.JoinByCodeAsync(userId, code.ToUpperInvariant());
        if (resolved is null)
            throw new HubException("Комната не найдена или закрыта.");

        var (room, role) = resolved.Value;
        await Groups.AddToGroupAsync(Context.ConnectionId, RoomGroup(room.Id));
        if (role == Domain.RoomRole.Streamer)
            await Groups.AddToGroupAsync(Context.ConnectionId, StreamerGroup(room.Id));

        // Присутствие — основа watch-time и Dead Man's Switch
        await _presence.JoinAsync(room.Id, userId, role, Context.ConnectionId);

        await BroadcastRosterAsync(room.Id);

        return RoomDto.From(room, role);
    }

    /// <summary>
    /// Зритель инициирует событие. Горячий путь, шаги по порядку:
    /// 1. проверка членства; 
    /// 2. авторитетная валидация события по серверному манифесту (белый список); 
    /// 3. валидация параметров; 
    /// 4. атомарное списание валюты; 
    /// 5. ПРЯМАЯ доставка команды стримеру; 
    /// 6. асинхронная публикация в
    /// очередь для журнала/аналитики (вне горячего пути). Возвращает зрителю
    /// признак приёма и новый баланс для мгновенного обновления UI
    /// </summary>
    public async Task<TriggerResult> TriggerEvent(string code, string eventId, Dictionary<string, string>? args, Guid? mediaId = null)
    {
        var userId = Context.User?.GetUserId()
            ?? throw new HubException("Не авторизован.");
        var username = Context.User?.FindFirst("displayName")?.Value ?? "viewer";
        var arguments = args ?? new Dictionary<string, string>();

        var resolved = await _rooms.ResolveMembershipAsync(userId, code.ToUpperInvariant());
        if (resolved is null)
            throw new HubException("Вы не состоите в этой комнате.");
        var (room, _) = resolved.Value;

        // Авторитетный белый список: вызвать можно только включённое событие
        // из манифеста игры этой комнаты
        var def = await _gameService.GetEventDefinitionAsync(room.GameId, eventId);
        if (def == null)
            throw new HubException("Событие недоступно в этой игре.");

        if (!ManifestValidation.ValidateParams(def, arguments, out var reason))
            throw new HubException(reason ?? "Неверные параметры события.");

        // Per-session управление: стример мог выключить это событие в комнате
        if (await _rooms.IsEventDisabledAsync(room.Id, eventId))
            throw new HubException("Событие выключено стримером.");

        // Глобально событие могло быть выключено администратором
        if (room.GameId is not null && await _rooms.IsEventGloballyDisabledAsync(room.GameId, eventId))
            throw new HubException("Событие отключено администратором.");

        // Прикреплённое медиа резолвим ДО списания (одобрение, обработка,
        // tolerance-фильтры, стоимость), чтобы не списать при невалидном медиа
        MediaPlayback? media = null;
        long mediaCost = 0;
        if (mediaId is not null)
        {
            var (ok, mediaError, playback, cost) = await _media.ResolveForPlaybackAsync(mediaId.Value, room.OwnerId);
            if (!ok)
                throw new HubException(mediaError ?? "Медиа недоступно.");
            media = playback;
            mediaCost = cost;
        }

        // Единое атомарное списание стоимости события и медиа с баланса зрителя
        // у стримера (room.OwnerId). Защита от перерасхода и гонок
        var operationId = Guid.NewGuid();
        var total = def.CostCoins + mediaCost;
        var charge = await _wallet.ChargeAsync(operationId, userId, room.OwnerId, room.Id, eventId, total);
        if (charge.Status == ChargeStatus.InsufficientFunds)
            throw new HubException("Недостаточно средств.");

        // Прямая доставка стримеру — через Redis backplane уходит на тот узел,
        // где висит соединение десктопа, без брокера в пути
        var command = new GameCommand(eventId, username) { Args = arguments, Media = media };
        await Clients.Group(StreamerGroup(room.Id)).SendAsync("GameCommand", command);

        // Журнал/аналитика — асинхронно, вне горячего пути
        await _bus.PublishAsync(new EventMessage
        {
            RoomId = room.Id,
            RoomCode = room.Code,
            UserId = userId,
            Username = username,
            EventId = eventId,
            Args = arguments,
            CostCoins = def.CostCoins,
            MediaId = mediaId
        });

        return new TriggerResult(true, charge.Balance);
    }

    /// <summary>
    /// Стример выгоняет (ban=false) или блокирует (ban=true) зрителя. Соединения
    /// зрителя получают "Kicked", присутствие снимается, ростер обновляется
    /// </summary>
    public async Task KickViewer(Guid roomId, Guid targetUserId, bool ban)
    {
        var userId = Context.User?.GetUserId()
            ?? throw new HubException("Не авторизован.");

        if (!await _rooms.KickAsync(userId, roomId, targetUserId, ban))
            throw new HubException("Недостаточно прав или нельзя выгнать этого участника.");

        var conns = await _presence.ConnectionIdsAsync(roomId, targetUserId);
        if (conns.Count > 0)
            await Clients.Clients(conns).SendAsync("Kicked", roomId);

        await _presence.RemoveUserAsync(roomId, targetUserId);
        await BroadcastRosterAsync(roomId);
    }

    /// <summary>
    /// Разослать актуальный ростер комнаты всем её участникам
    /// </summary>
    private async Task BroadcastRosterAsync(Guid roomId)
    {
        var roster = await _presence.GetRosterAsync(roomId);
        await Clients.Group(RoomGroup(roomId)).SendAsync("RoomRoster", roster);
    }

    /// <summary>
    /// При разрыве соединения снимаем присутствие. Если ушло последнее
    /// соединение стримера — срабатывает Dead Man's Switch: открытые ставки
    /// комнаты отменяются с возвратом средств зрителям
    /// </summary>
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var left = await _presence.LeaveAsync(Context.ConnectionId);
        if (left is not null)
        {
            var (roomId, role) = left.Value;
            if (role == Domain.RoomRole.Streamer && !await _presence.HasStreamerAsync(roomId))
                await _betting.CancelOpenBetsForRoomAsync(roomId, "streamer_left");

            // Кто-то отключился — рассылаем обновлённый ростер
            await BroadcastRosterAsync(roomId);
        }

        await base.OnDisconnectedAsync(exception);
    }
}

/// <summary>
/// Ответ зрителю на инициированное событие: принято ли и новый баланс
/// </summary>
public sealed record TriggerResult(bool Accepted, long Balance);