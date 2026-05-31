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
    private readonly Social.OnlinePresenceService _online;
    private readonly EventCooldownService _cooldown;
    private readonly BridgeReadinessService _bridge;
    private readonly MediaChargeService _mediaCharges;
    private readonly MediaVolumeService _volume;

    public RoomHub(RoomService rooms, ManifestCatalog catalog, WalletService wallet,
        PresenceService presence, BettingService betting, MediaService media, IEventBus bus,
        GameService gameService, Social.OnlinePresenceService online, EventCooldownService cooldown,
        BridgeReadinessService bridge, MediaChargeService mediaCharges, MediaVolumeService volume)
    {
        _rooms = rooms;
        _catalog = catalog;
        _wallet = wallet;
        _presence = presence;
        _betting = betting;
        _media = media;
        _bus = bus;
        _gameService = gameService;
        _online = online;
        _cooldown = cooldown;
        _bridge = bridge;
        _mediaCharges = mediaCharges;
        _volume = volume;
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

        // Активность сменилась (вошёл в комнату -> "Играет в ..." / "Смотрит ...") —
        // уведомляем друзей, чтобы статус обновился в реальном времени
        await _online.NotifyActivityChangedAsync(userId);

        // Текущая громкость медиа — чтобы оверлей/десктоп сразу синхронизировались
        await Clients.Caller.SendAsync("MediaVolumeChanged", _volume.Get(room.Id));

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
    public async Task<TriggerResult> TriggerEvent(string code, string eventId, Dictionary<string, string>? args, string? mediaId = null)
    {
        var userId = Context.User?.GetUserId()
            ?? throw new HubException("Не авторизован.");
        var username = Context.User?.FindFirst("displayName")?.Value ?? "viewer";
        var arguments = args ?? new Dictionary<string, string>();

        Guid? mediaGuid = null;
        if (!string.IsNullOrWhiteSpace(mediaId) && Guid.TryParse(mediaId, out var parsedMediaId))
            mediaGuid = parsedMediaId;

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

        // Защита баллов: если мост стримера с игрой не готов, команда всё равно
        // не выполнится — не принимаем событие и не списываем валюту
        if (!_bridge.IsReady(room.Id))
            throw new HubException("Стример сейчас не принимает события (игра не подключена).");

        // Прикреплённое медиа резолвим ДО списания (одобрение, обработка,
        // tolerance-фильтры, стоимость), чтобы не списать при невалидном медиа
        MediaPlayback? media = null;
        long mediaCost = 0;
        if (mediaGuid is not null)
        {
            var (ok, mediaError, playback, cost) = await _media.ResolveForPlaybackAsync(mediaGuid.Value, room.OwnerId);
            if (!ok)
                throw new HubException(mediaError ?? "Медиа недоступно.");
            media = playback;
            mediaCost = cost;
        }

        // Владелец комнаты (стример) — валюта бесконечна: не списываем и не
        // ограничиваем перезарядкой
        var isOwner = userId == room.OwnerId;

        // Перезарядка: не даём спамить одним событием чаще cooldown. Проверяем
        // перед списанием — спам не сжигает баланс и не шлёт бесполезные
        // команды
        if (!isOwner)
        {
            var (allowed, remainingMs) = _cooldown.TryTrigger(room.Id, userId, eventId, def.CooldownMs);
            if (!allowed)
                throw new HubException($"Событие на перезарядке. Подождите {Math.Ceiling(remainingMs / 1000.0)} с.");
        }

        // Единое атомарное списание стоимости события и медиа с баланса зрителя
        // у стримера (room.OwnerId). Защита от перерасхода и гонок
        long balance = long.MaxValue; // ∞ для владельца
        if (!isOwner)
        {
            var operationId = Guid.NewGuid();
            var total = def.CostCoins + mediaCost;
            var charge = await _wallet.ChargeAsync(operationId, userId, room.OwnerId, room.Id, eventId, total);
            if (charge.Status == ChargeStatus.InsufficientFunds)
                throw new HubException("Недостаточно средств.");
            balance = charge.Balance;
        }

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
            MediaId = mediaGuid
        });

        return new TriggerResult(true, balance);
    }

    /// <summary>
    /// Зритель отправляет медиа стримеру (проигрывается в оверлее на стриме)
    /// Отдельно от игровых событий: не требует игрового события и НЕ зависит от
    /// готовности игрового моста — медиа проигрывает оверлей. Списывает только
    /// стоимость медиа. Возвращает новый баланс
    /// </summary>
    public async Task<TriggerResult> SendMedia(string code, string mediaId)
    {
        var userId = Context.User?.GetUserId()
            ?? throw new HubException("Не авторизован.");
        var username = Context.User?.FindFirst("displayName")?.Value ?? "viewer";

        if (!Guid.TryParse(mediaId, out var mediaGuid))
            throw new HubException("Некорректный идентификатор медиа.");

        var resolved = await _rooms.ResolveMembershipAsync(userId, code.ToUpperInvariant());
        if (resolved is null)
            throw new HubException("Вы не состоите в этой комнате.");
        var (room, _) = resolved.Value;

        // Резолвим медиа ДО списания (одобрение/обработка/фильтры/стоимость)
        var (ok, mediaError, playback, cost) = await _media.ResolveForPlaybackAsync(mediaGuid, room.OwnerId);
        if (!ok || playback is null)
            throw new HubException(mediaError ?? "Медиа недоступно.");

        // Подпись для оверлея: кто отправил
        playback.SenderName = username;

        // Владелец комнаты — валюта бесконечна, не списываем
        long balance = long.MaxValue;
        if (userId != room.OwnerId && cost > 0)
        {
            var chargeId = Guid.NewGuid();
            var charge = await _wallet.ChargeAsync(chargeId, userId, room.OwnerId, room.Id, "media", cost);
            if (charge.Status == ChargeStatus.InsufficientFunds)
                throw new HubException("Недостаточно средств.");
            balance = charge.Balance;
            // Запоминаем списание, чтобы вернуть баллы при сбое воспроизведения
            _mediaCharges.Register(chargeId, userId, room.OwnerId, room.Id, cost);
            playback.ChargeId = chargeId.ToString();
        }

        // Доставляем оверлею стримера отдельным сообщением. Десктоп его не слушает
        // (это не игровая команда), поэтому мост его не отклоняет
        await Clients.Group(StreamerGroup(room.Id)).SendAsync("MediaPlayback", playback);

        await _bus.PublishAsync(new EventMessage
        {
            RoomId = room.Id,
            RoomCode = room.Code,
            UserId = userId,
            Username = username,
            EventId = "media",
            Args = new Dictionary<string, string>(),
            CostCoins = cost,
            MediaId = mediaGuid
        });

        return new TriggerResult(true, balance);
    }

    /// <summary>
    /// Оверлей стримера сообщает, что медиа не воспроизвелось — возвращаем баллы
    /// зрителю (идемпотентно по chargeId). Вызывать может только владелец комнаты
    /// </summary>
    public async Task ReportMediaFailed(string chargeId)
    {
        var userId = Context.User?.GetUserId();
        if (userId is null || !Guid.TryParse(chargeId, out var id))
            return;
        if (!_mediaCharges.TryConsume(id, out var c))
            return; // уже возвращено или неизвестно
        // Возврат разрешаем только владельцу комнаты, к которой относится списание
        var room = await _rooms.GetAsync(c.RoomId);
        if (room is null || room.OwnerId != userId.Value)
            return;
        await _wallet.CreditAsync(DeterministicGuid.Create($"media_refund:{id}"),
            c.UserId, c.StreamerId, c.RoomId, "media_refund", c.Cost);
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
    /// Задать громкость медиа (0-100). Только владелец. Рассылается
    /// всем соединениям стримера (десктоп + оверлей) для синхронизации регуляторов
    /// </summary>
    public async Task SetMediaVolume(Guid roomId, int volume)
    {
        var userId = Context.User?.GetUserId();
        if (userId is null) return;
        var room = await _rooms.GetAsync(roomId);
        if (room is null || room.OwnerId != userId.Value) return;
        var v = _volume.Set(roomId, volume);
        await Clients.Group(StreamerGroup(roomId)).SendAsync("MediaVolumeChanged", v);
    }

    /// <summary>
    /// Десктоп стримера сообщает готовность игрового моста (heartbeat). Пока мост
    /// не готов, события зрителей отклоняются без списания баллов
    /// </summary>
    public async Task ReportBridgeReady(Guid roomId, bool ready)
    {
        var userId = Context.User?.GetUserId();
        if (userId is null) return;
        var room = await _rooms.GetAsync(roomId);
        if (room is null || room.OwnerId != userId.Value) return; // только владелец
        _bridge.Report(roomId, ready);
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
            {
                await _betting.CancelOpenBetsForRoomAsync(roomId, "streamer_left");
                _bridge.Clear(roomId); // мост ушёл вместе со стримером
            }

            // Кто-то отключился — рассылаем обновлённый ростер
            await BroadcastRosterAsync(roomId);

            // Активность сменилась (вышел из комнаты) — обновляем статус у друзей
            var userId = Context.User?.GetUserId();
            if (userId is not null)
                await _online.NotifyActivityChangedAsync(userId.Value);
        }

        await base.OnDisconnectedAsync(exception);
    }
}

/// <summary>
/// Ответ зрителю на инициированное событие: принято ли и новый баланс
/// </summary>
public sealed record TriggerResult(bool Accepted, long Balance);