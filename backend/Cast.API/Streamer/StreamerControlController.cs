using Cast.API.Common;
using Cast.API.Data;
using Cast.API.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Cast.API.Streamer;

public sealed record TagFiltersDto(FilterMode Mode, List<string> Tags);
public sealed record SetTagFiltersRequest(FilterMode Mode, List<string> Tags);

[ApiController]
[Route("api/streamer/filters")]
[Authorize]
public sealed class StreamerControlController : ControllerBase
{
    private readonly CastDbContext _db;

    public StreamerControlController(CastDbContext db) => _db = db;

    /// <summary>
    /// Текущий режим и набор тегов tolerance-фильтра стримера
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<TagFiltersDto>> Get(CancellationToken ct)
    {
        var me = User.GetUserId();
        if (me is null) return Unauthorized();

        var settings = await _db.StreamerFilterSettings.FindAsync(new object?[] { me.Value }, ct);
        var tags = await _db.StreamerTagFilters
            .Where(f => f.StreamerId == me.Value)
            .Select(f => f.Tag)
            .ToListAsync(ct);
        return Ok(new TagFiltersDto(settings?.Mode ?? FilterMode.Blocklist, tags));
    }

    /// <summary>
    /// Заменить режим и набор тегов фильтра
    /// </summary>
    [HttpPut]
    public async Task<ActionResult<TagFiltersDto>> Set(SetTagFiltersRequest req, CancellationToken ct)
    {
        var me = User.GetUserId();
        if (me is null) return Unauthorized();

        var tags = (req.Tags ?? new())
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim().ToLowerInvariant())
            .Distinct()
            .ToList();

        var settings = await _db.StreamerFilterSettings.FindAsync(new object?[] { me.Value }, ct);
        if (settings is null)
        {
            settings = new StreamerFilterSettings { StreamerId = me.Value, Mode = req.Mode };
            _db.StreamerFilterSettings.Add(settings);
        }
        else
        {
            settings.Mode = req.Mode;
        }

        await _db.StreamerTagFilters.Where(f => f.StreamerId == me.Value).ExecuteDeleteAsync(ct);
        _db.StreamerTagFilters.AddRange(tags.Select(t => new StreamerTagFilter { StreamerId = me.Value, Tag = t }));
        await _db.SaveChangesAsync(ct);

        return Ok(new TagFiltersDto(req.Mode, tags));
    }
}