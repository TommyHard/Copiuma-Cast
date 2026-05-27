using Cast.API.Common;
using Cast.API.Domain;
using Cast.API.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Cast.API.Media;

[ApiController]
[Route("api/media")]
[Authorize]
public sealed class MediaController : ControllerBase
{
    private const long MaxUploadBytes = 50 * 1024 * 1024; // 50 MB

    private readonly MediaService _media;
    private readonly StorageService _storage;

    public MediaController(MediaService media, StorageService storage)
    {
        _media = media;
        _storage = storage;
    }

    /// <summary>
    /// Загрузить медиа (звук/видео). Запись создаётся в статусе Pending
    /// </summary>
    [HttpPost]
    [RequestSizeLimit(MaxUploadBytes)]
    public async Task<ActionResult<MediaDto>> Upload([FromForm] IFormFile file, [FromForm] string title, [FromForm] MediaType type)
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();

        if (file is null || file.Length == 0)
            return BadRequest("Файл не передан.");
        if (file.Length > MaxUploadBytes)
            return BadRequest("Файл больше 50 МБ.");

        var expectedPrefix = type == MediaType.Sound ? "audio/" : "video/";
        if (string.IsNullOrEmpty(file.ContentType) || !file.ContentType.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
            return BadRequest($"Для типа {type} ожидается {expectedPrefix}* контент.");
        if (string.IsNullOrWhiteSpace(title))
            return BadRequest("Укажите название.");

        var ext = Path.GetExtension(file.FileName);
        var key = $"media/{type.ToString().ToLowerInvariant()}/{userId}/{Guid.NewGuid():N}{ext}";
        await using (var stream = file.OpenReadStream())
            await _storage.UploadMediaAsync(stream, key, file.ContentType);

        var dto = await _media.CreateAsync(userId.Value, title, type, key);
        return Ok(dto);
    }

    /// <summary>
    /// Моя медиатека
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<MediaDto>>> MyLibrary(CancellationToken ct)
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();
        return Ok(await _media.MyLibraryAsync(userId.Value, ct));
    }

    /// <summary>
    /// Карточка медиа
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<MediaDto>> Get(Guid id, CancellationToken ct)
    {
        var dto = await _media.GetAsync(id, ct);
        return dto is null ? NotFound() : Ok(dto);
    }

    /// <summary>
    /// Изменить монтаж/позицию своего медиа
    /// </summary>
    [HttpPut("{id:guid}/edit")]
    public async Task<IActionResult> Edit(Guid id, EditMediaRequest req, CancellationToken ct)
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();
        return await _media.EditAsync(userId.Value, id, req, ct) ? NoContent() : NotFound();
    }

    // ---- Модерация (админ) ----

    /// <summary>
    /// Очередь на модерацию
    /// </summary>
    [HttpGet("moderation/pending")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<List<MediaDto>>> Pending(CancellationToken ct)
        => Ok(await _media.PendingAsync(ct));

    /// <summary>
    /// Одобрить медиа и назначить теги
    /// </summary>
    [HttpPost("{id:guid}/approve")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Approve(Guid id, ApproveMediaRequest req, CancellationToken ct)
    {
        var adminId = User.GetUserId();
        if (adminId is null) return Unauthorized();
        return await _media.ApproveAsync(adminId.Value, id, req.Tags ?? new(), req.CostCoins, ct) ? Ok() : NotFound();
    }

    /// <summary>
    /// Отклонить медиа
    /// </summary>
    [HttpPost("{id:guid}/reject")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Reject(Guid id, CancellationToken ct)
    {
        var adminId = User.GetUserId();
        if (adminId is null) return Unauthorized();
        return await _media.RejectAsync(adminId.Value, id, ct) ? Ok() : NotFound();
    }
}