using Cast.API.Common;
using Cast.API.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Cast.API.Games;

[ApiController]
[Route("api/games")]
[Authorize]
public sealed class GamesController : ControllerBase
{
    private readonly GameService _games;

    public GamesController(GameService games) => _games = games;

    /// <summary>
    /// Каталог поддерживаемых игр (карточки)
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<GameCardDto>>> Catalog(CancellationToken ct)
        => Ok(await _games.GetCatalogAsync(ct));

    /// <summary>
    /// Страница игры: карточка, доступные события, личная и глобальная статистика
    /// </summary>
    [HttpGet("{slug}")]
    public async Task<ActionResult<GameDetailDto>> Detail(string slug, CancellationToken ct)
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();

        var isAdmin = User.IsInRole("Admin");

        var detail = await _games.GetDetailAsync(slug, userId.Value, isAdmin, ct);
        return detail is null ? NotFound("Игра не найдена.") : Ok(detail);
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<GameDetailDto>> Create([FromForm] GameUpsertDto req, [FromServices] StorageService storage, CancellationToken ct)
    {
        return Ok(await _games.UpsertAsync(null, req, storage, ct));
    }

    [HttpPut("{slug}")]
    [Authorize(Roles = "Admin")]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<GameDetailDto>> Update(string slug, [FromForm] GameUpsertDto req, [FromServices] StorageService storage, CancellationToken ct)
    {
        return Ok(await _games.UpsertAsync(slug, req, storage, ct));
    }

    [HttpDelete("{slug}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(string slug, CancellationToken ct)
    {
        await _games.DeleteAsync(slug, ct);
        return NoContent();
    }

    [HttpPost("{slug}/toggle")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Toggle(string slug, CancellationToken ct)
    {
        await _games.ToggleAsync(slug, ct);
        return Ok();
    }

    [HttpPut("{slug}/events/{eventId}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> SetGlobalEventEnabled(string slug, string eventId, [FromBody] bool enabled, [FromServices] Data.CastDbContext db, CancellationToken ct)
    {
        var overrideObj = await db.GameEventOverrides
            .FirstOrDefaultAsync(o => o.GameId == slug && o.EventId == eventId, ct);

        if (overrideObj == null)
        {
            overrideObj = new Domain.GameEventOverride { GameId = slug, EventId = eventId, Enabled = enabled };
            db.GameEventOverrides.Add(overrideObj);
        }
        else
        {
            overrideObj.Enabled = enabled;
        }

        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}