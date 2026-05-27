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
    private readonly ManifestCatalog _catalog;

    public RoomsController(RoomService rooms, ManifestCatalog catalog)
    {
        _rooms = rooms;
        _catalog = catalog;
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
        var events = _catalog.GetEvents(room.GameId)
            .Select(e => new RoomEventDto(e.Id, e.Title, e.Category, e.CostCoins, e.CooldownMs,
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
}