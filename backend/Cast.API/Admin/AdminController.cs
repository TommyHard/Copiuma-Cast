using Cast.API.Common;
using Cast.API.Data;
using Cast.API.Domain;
using Cast.API.Games;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Cast.API.Admin;

/// <summary>
/// Админ-панель: блокировка пользователей, рассмотрение заявок на
/// статус стримера, модерация медиа
/// </summary>
[ApiController]
[Route("api/admin")]
[Authorize(Roles = "Admin")]
public sealed class AdminController : ControllerBase
{
    public const string StreamerRole = "Streamer";

    private readonly CastDbContext _db;
    private readonly UserManager<ApplicationUser> _users;
    private readonly ManifestCatalog _catalog;

    public AdminController(CastDbContext db, UserManager<ApplicationUser> users, ManifestCatalog catalog)
    {
        _db = db;
        _users = users;
        _catalog = catalog;
    }

    // ---- Пользователи ----

    [HttpPost("users/{userId:guid}/block")]
    public Task<IActionResult> Block(Guid userId, CancellationToken ct) => SetBlocked(userId, true);

    [HttpPost("users/{userId:guid}/unblock")]
    public Task<IActionResult> Unblock(Guid userId, CancellationToken ct) => SetBlocked(userId, false);

    private async Task<IActionResult> SetBlocked(Guid userId, bool blocked)
    {
        var user = await _users.FindByIdAsync(userId.ToString());
        if (user is null) return NotFound("Пользователь не найден.");
        user.IsBlocked = blocked;
        await _users.UpdateAsync(user);
        return NoContent();
    }

    [HttpGet("users")]
    public async Task<ActionResult<List<AdminUserDto>>> Users([FromQuery] string? search, [FromQuery] int skip = 0, [FromQuery] int take = 50)
    {
        var q = _users.Users.AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            q = q.Where(u => u.Email!.ToLower().Contains(s) || u.Handle.ToLower().Contains(s) || u.DisplayName.ToLower().Contains(s));
        }
        var list = await q.OrderBy(u => u.Handle)
            .Skip(Math.Max(0, skip)).Take(Math.Clamp(take, 1, 200))
            .Select(u => new AdminUserDto(u.Id, u.Email ?? string.Empty, u.DisplayName, u.Handle, u.IsBlocked))
            .ToListAsync();
        return Ok(list);
    }

    // ---- Заявки на статус стримера ----

    [HttpGet("applications")]
    public async Task<ActionResult<List<StreamerApplicationDto>>> Applications(CancellationToken ct)
        => Ok(await (from a in _db.StreamerApplications
                     where a.Status == ApplicationStatus.Pending
                     join u in _db.Users on a.UserId equals u.Id
                     orderby a.CreatedAt
                     select new StreamerApplicationDto(a.Id, u.Id, u.DisplayName, u.Handle, a.Message, a.Status, a.CreatedAt))
            .ToListAsync(ct));

    [HttpPost("applications/{id:guid}/approve")]
    public async Task<IActionResult> Approve(Guid id, CancellationToken ct)
    {
        var app = await _db.StreamerApplications.FirstOrDefaultAsync(a => a.Id == id && a.Status == ApplicationStatus.Pending, ct);
        if (app is null) return NotFound("Заявка не найдена.");

        var user = await _users.FindByIdAsync(app.UserId.ToString());
        if (user is null) return NotFound("Пользователь не найден.");
        if (!await _users.IsInRoleAsync(user, StreamerRole))
            await _users.AddToRoleAsync(user, StreamerRole);

        app.Status = ApplicationStatus.Approved;
        app.ReviewedBy = User.GetUserId();
        app.ReviewedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("applications/{id:guid}/reject")]
    public async Task<IActionResult> Reject(Guid id, CancellationToken ct)
    {
        var app = await _db.StreamerApplications.FirstOrDefaultAsync(a => a.Id == id && a.Status == ApplicationStatus.Pending, ct);
        if (app is null) return NotFound("Заявка не найдена.");
        app.Status = ApplicationStatus.Rejected;
        app.ReviewedBy = User.GetUserId();
        app.ReviewedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }
}