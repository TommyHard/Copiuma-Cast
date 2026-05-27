using Cast.API.Common;
using Cast.API.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Cast.API.Bets;

[ApiController]
[Route("api/rooms/{roomId:guid}/bets")]
[Authorize]
public sealed class BetsController : ControllerBase
{
    private readonly BettingService _betting;
    private readonly CastDbContext _db;

    public BetsController(BettingService betting, CastDbContext db)
    {
        _betting = betting;
        _db = db;
    }

    /// <summary>
    /// Список ставок комнаты с пулами и коэффициентами
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<BetDto>>> List(Guid roomId, CancellationToken ct)
        => Ok(await BetDto.LoadRoomAsync(_db, roomId, ct));

    /// <summary>
    /// Создать ставку (только владелец комнаты)
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<BetDto>> Create(Guid roomId, CreateBetRequest req, CancellationToken ct)
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();
        if (!await IsOwnerAsync(roomId, userId.Value, ct)) return Forbid();
        if (string.IsNullOrWhiteSpace(req.Title) || req.Outcomes is not { Count: >= 2 })
            return BadRequest("Нужны заголовок и минимум два исхода.");

        var bet = await _betting.CreateBetAsync(userId.Value, roomId, req.Title, req.Outcomes, req.LocksInSeconds, ct);
        return Ok(await BetDto.LoadAsync(_db, bet.Id, ct));
    }

    /// <summary>
    /// Сделать ставку (зритель)
    /// </summary>
    [HttpPost("{betId:guid}/wagers")]
    public async Task<IActionResult> Place(Guid roomId, Guid betId, PlaceWagerRequest req, CancellationToken ct)
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();

        var (error, balance) = await _betting.PlaceWagerAsync(userId.Value, betId, req.OutcomeId, req.Amount, ct);
        return error switch
        {
            WagerError.None => Ok(new { balance }),
            WagerError.BetNotFound => NotFound("Ставка не найдена."),
            WagerError.BetClosed => Conflict("Приём ставок закрыт."),
            WagerError.UnknownOutcome => BadRequest("Неизвестный исход."),
            WagerError.InvalidAmount => BadRequest("Некорректная сумма."),
            WagerError.InsufficientFunds => BadRequest(new { error = "Недостаточно средств.", balance }),
            _ => BadRequest()
        };
    }

    /// <summary>
    /// Разрешить ставку, указав выигравший исход (только владелец)
    /// </summary>
    [HttpPost("{betId:guid}/resolve")]
    public async Task<IActionResult> Resolve(Guid roomId, Guid betId, ResolveBetRequest req, CancellationToken ct)
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();

        var ok = await _betting.ResolveBetAsync(userId.Value, betId, req.WinningOutcomeId, ct);
        return ok ? Ok(await BetDto.LoadAsync(_db, betId, ct))
                  : Conflict("Ставку нельзя разрешить (нет прав, уже закрыта или неверный исход).");
    }

    /// <summary>
    /// Отменить ставку с возвратом средств (только владелец)
    /// </summary>
    [HttpPost("{betId:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid roomId, Guid betId, CancellationToken ct)
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();
        var ok = await _betting.CancelBetAsync(userId.Value, betId, "manual", ct);
        return ok ? Ok() : Conflict("Ставку нельзя отменить (нет прав или уже закрыта).");
    }

    private Task<bool> IsOwnerAsync(Guid roomId, Guid userId, CancellationToken ct)
        => _db.Rooms.AnyAsync(r => r.Id == roomId && r.OwnerId == userId, ct);
}