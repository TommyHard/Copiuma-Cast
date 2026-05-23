using Cast.API.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Cast.API.Rooms;

[ApiController]
[Route("api/rooms")]
[Authorize]
public sealed class RoomsController : ControllerBase
{
    private readonly RoomService _rooms;

    public RoomsController(RoomService rooms) => _rooms = rooms;

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
    /// Войти в комнату по коду (зритель добавляется автоматически).
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
}
