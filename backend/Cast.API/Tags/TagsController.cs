using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Cast.API.Tags;

/// <summary>
/// Справочник тегов: поиск существующих (подсказки) и создание новых.
///   GET  /api/tags?query=  — поиск/подсказки
///   POST /api/tags         — создать новый тег (идемпотентно)
/// </summary>
[ApiController]
[Route("api/tags")]
[Authorize]
public sealed class TagsController : ControllerBase
{
    private readonly TagService _tags;

    public TagsController(TagService tags) => _tags = tags;

    [HttpGet]
    public async Task<ActionResult<List<string>>> Search([FromQuery] string? query, CancellationToken ct)
        => Ok(await _tags.SearchAsync(query, 20, ct));

    [HttpPost]
    public async Task<ActionResult<string>> Create([FromBody] CreateTagRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Name))
            return BadRequest("Пустой тег.");
        var names = await _tags.EnsureAsync(new[] { req.Name }, ct);
        return Ok(names.FirstOrDefault() ?? string.Empty);
    }
}

public sealed record CreateTagRequest(string Name);
