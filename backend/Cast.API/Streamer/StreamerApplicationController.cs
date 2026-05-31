using Cast.API.Common;
using Cast.API.Data;
using Cast.API.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Cast.API.Streamer;

public sealed record ApplyStreamerRequest(string? Message);
public sealed record MyApplicationDto(Guid Id, ApplicationStatus Status, string? Message, DateTimeOffset CreatedAt);

[ApiController]
[Route("api/streamer/application")]
[Authorize]
public sealed class StreamerApplicationController : ControllerBase
{
    private readonly CastDbContext _db;

    public StreamerApplicationController(CastDbContext db) => _db = db;

    /// <summary>
    /// Подать заявку на статус стримера
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Apply(ApplyStreamerRequest req, CancellationToken ct)
    {
        var me = User.GetUserId();
        if (me is null) return Unauthorized();

        if (User.IsInRole(AdminConstants.StreamerRole))
            return Conflict("Вы уже стример.");
        if (await _db.StreamerApplications.AnyAsync(a => a.UserId == me.Value && a.Status == ApplicationStatus.Pending, ct))
            return Conflict("Заявка уже на рассмотрении.");

        _db.StreamerApplications.Add(new StreamerApplication { UserId = me.Value, Message = req.Message?.Trim() });
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>
    /// Моя последняя заявка и её статус
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<MyApplicationDto>> Mine(CancellationToken ct)
    {
        var me = User.GetUserId();
        if (me is null) return Unauthorized();

        var app = await _db.StreamerApplications
            .Where(a => a.UserId == me.Value)
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => new MyApplicationDto(a.Id, a.Status, a.Message, a.CreatedAt))
            .FirstOrDefaultAsync(ct);
        // 204 вместо 404: "нет заявки" — ок состояние
        return app is null ? NoContent() : Ok(app);
    }
}

/// <summary>
/// Имена ролей (общие константы)
/// </summary>
public static class AdminConstants
{
    public const string AdminRole = "Admin";
    public const string StreamerRole = "Streamer";
}