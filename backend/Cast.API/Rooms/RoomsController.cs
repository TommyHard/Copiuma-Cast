using Cast.API.Common;
using Cast.API.Games;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Cast.API.Rooms;

[ApiController]
[Route("api/rooms")]
[Authorize]
public sealed class RoomsController : ControllerBase
{
    private readonly RoomService _rooms;
    private readonly GameService _games;

    public RoomsController(RoomService rooms, GameService games)
    {
        _rooms = rooms;
        _games = games;
    }

    /// <summary>
    /// Создать комнату (текущий пользователь — стример-владелец)
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<RoomDto>> Create(CreateRoomRequest req, CancellationToken ct)
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();

        var room = await _rooms.CreateAsync(userId.Value, req, ct);
        return Ok(RoomDto.From(room, Domain.RoomRole.Streamer));
    }

    /// <summary>
    /// Мои комнаты: открытые комнаты, где я участник (в т.ч. по приглашению).
    /// Используется веб-клиентом, чтобы показать приглашения и войти в них
    /// </summary>
    [HttpGet("mine")]
    public async Task<ActionResult<List<RoomDto>>> Mine(CancellationToken ct)
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();
        return Ok(await _rooms.MyRoomsAsync(userId.Value, ct));
    }

    /// <summary>
    /// Отклонить приглашение в комнату (удаляет ожидающее членство)
    /// </summary>
    [HttpPost("{roomId:guid}/decline")]
    public async Task<IActionResult> Decline(Guid roomId, CancellationToken ct)
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();
        return await _rooms.DeclineInviteAsync(userId.Value, roomId, ct) ? Ok() : NotFound("Приглашение не найдено.");
    }

    /// <summary>
    /// Войти в комнату по коду (зритель добавляется автоматически)
    ///</summary>
    [HttpPost("{code}/join")]
    public async Task<ActionResult<RoomDto>> Join(string code, CancellationToken ct)
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();

        var result = await _rooms.JoinByCodeAsync(userId.Value, code.ToUpperInvariant(), ct);
        if (result is null) return NotFound("Комната не найдена или закрыта.");

        return Ok(RoomDto.From(result.Value.room, result.Value.role));
    }

    /// <summary>
    /// Список событий комнаты с учётом включения/выключения стримером для текущей
    /// сессии (доступные команды)
    /// </summary>
    [HttpGet("{roomId:guid}/events")]
    public async Task<ActionResult<List<RoomEventDto>>> Events(Guid roomId, CancellationToken ct)
    {
        var room = await _rooms.GetAsync(roomId, ct);
        if (room is null) return NotFound("Комната не найдена.");

        var disabled = await _rooms.DisabledEventsAsync(roomId, ct);
        var globallyDisabled = await _rooms.GloballyDisabledEventsAsync(room.GameId, ct);
        var events = (await _games.GetInteractionsAsync(room.GameId, ct))
            .Select(e => new RoomEventDto(e.Id, e.Title, e.Description, e.Category, e.CostCoins, e.CooldownMs,
                e.Enabled && !disabled.Contains(e.Id) && !globallyDisabled.Contains(e.Id)))
            .ToList();
        return Ok(events);
    }

    /// <summary>
    /// Включить/выключить событие в комнате на текущую сессию (только владелец)
    /// </summary>
    [HttpPut("{roomId:guid}/events/{eventId}")]
    public async Task<IActionResult> SetEventEnabled(Guid roomId, string eventId, SetEventEnabledRequest req, CancellationToken ct)
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();

        var ok = await _rooms.SetEventEnabledAsync(userId.Value, roomId, eventId, req.Enabled, ct);
        return ok ? NoContent() : Forbid();
    }

    /// <summary>
    /// Баланс текущего зрителя у стримера этой комнаты (валюта локализована по
    /// стримеру). Используется UI комнаты для отображения баланса
    /// </summary>
    [HttpGet("{roomId:guid}/balance")]
    public async Task<ActionResult<long>> Balance(Guid roomId, [FromServices] Economy.WalletService wallet, CancellationToken ct)
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();

        var room = await _rooms.GetAsync(roomId, ct);
        if (room is null) return NotFound("Комната не найдена.");

        var balance = await wallet.GetOrCreateBalanceAsync(userId.Value, room.OwnerId, ct);
        return Ok(balance);
    }

    /// <summary>
    /// Выдать монеты зрителю в кошельке этого стримера (только владелец комнаты).
    /// Валюта локальна для стримера — выдаётся именно своим зрителям
    /// </summary>
    [HttpPost("{roomId:guid}/grant")]
    public async Task<ActionResult<long>> Grant(Guid roomId, GrantCoinsRequest req,
        [FromServices] Economy.WalletService wallet, CancellationToken ct)
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();
        if (req.Amount <= 0) return BadRequest("Сумма должна быть положительной.");

        var room = await _rooms.GetAsync(roomId, ct);
        if (room is null) return NotFound("Комната не найдена.");
        if (room.OwnerId != userId.Value) return Forbid();

        var balance = await wallet.CreditAsync(Guid.NewGuid(), req.TargetUserId, room.OwnerId,
            roomId, "streamer_grant", req.Amount, ct);
        return Ok(balance);
    }

    /// <summary>
    /// Закрыть комнату (только владелец)
    /// </summary>
    [HttpPost("{roomId:guid}/close")]
    public async Task<IActionResult> Close(Guid roomId, CancellationToken ct)
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();
        return await _rooms.CloseAsync(userId.Value, roomId, ct) ? Ok() : Forbid();
    }

    /// <summary>
    /// Пригласить зрителя по @identifier (только владелец)
    /// </summary>
    [HttpPost("{roomId:guid}/invite")]
    public async Task<IActionResult> Invite(Guid roomId, InviteRequest req, CancellationToken ct)
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();

        var result = await _rooms.InviteByHandleAsync(userId.Value, roomId, req.Identifier ?? string.Empty, ct);
        return result switch
        {
            null => Forbid(),
            false => NotFound("Пользователь не найден."),
            _ => Ok()
        };
    }

    /// <summary>
    /// Ссылка-приглашение в комнату (для копирования). Открыв её, зритель
    /// автоматически входит в комнату по коду
    /// </summary>
    [HttpGet("{roomId:guid}/invite-link")]
    public async Task<ActionResult<InviteLinkDto>> InviteLink(Guid roomId, [FromServices] IConfiguration config, CancellationToken ct)
    {
        var room = await _rooms.GetAsync(roomId, ct);
        if (room is null) return NotFound("Комната не найдена.");

        var baseUrl = config["App:WebBaseUrl"]
            ?? config.GetSection("Cors:AllowedOrigins").Get<string[]>()?.FirstOrDefault()
            ?? $"{Request.Scheme}://{Request.Host}";

        var link = $"{baseUrl.TrimEnd('/')}/rooms?code={room.Code}";
        return Ok(new InviteLinkDto(link));
    }
}
