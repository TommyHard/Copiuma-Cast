using Cast.API.Common;
using Cast.API.Data;
using Cast.API.Domain;
using Cast.API.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

namespace Cast.API.News;

[ApiController]
[Route("api/news")]
[Authorize]
public sealed class NewsController : ControllerBase
{
    private readonly CastDbContext _db;
    private readonly StorageService _storage;

    public NewsController(CastDbContext db, StorageService storage)
    {
        _db = db;
        _storage = storage;
    }

    [HttpGet]
    public async Task<ActionResult<List<NewsDto>>> Published(CancellationToken ct)
    {
        var query = from n in _db.News.AsNoTracking()
                    where n.Published
                    join u in _db.Users on n.AuthorId equals u.Id
                    orderby n.CreatedAt descending
                    select new NewsDto(n.Id, n.Title, n.Body, n.ImageUrl, n.Published, n.CreatedAt, n.UpdatedAt, u.DisplayName, u.AvatarUrl);

        return Ok(await query.ToListAsync(ct));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<NewsDto>> Get(Guid id, CancellationToken ct)
    {
        var query = from n in _db.News.AsNoTracking()
                    where n.Id == id
                    join u in _db.Users on n.AuthorId equals u.Id
                    select new NewsDto(n.Id, n.Title, n.Body, n.ImageUrl, n.Published, n.CreatedAt, n.UpdatedAt, u.DisplayName, u.AvatarUrl);

        var dto = await query.FirstOrDefaultAsync(ct);
        if (dto is null) return NotFound();
        if (!dto.Published && !User.IsInRole("Admin")) return Forbid();

        return Ok(dto);
    }

    [HttpPost("image")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult> UploadImage([FromForm] IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0) return BadRequest();
        var key = $"news/{Guid.NewGuid():N}{Path.GetExtension(file.FileName)}";
        await using var stream = file.OpenReadStream();
        var url = await _storage.UploadPublicAsync(stream, key, file.ContentType, ct);
        return Ok(new { url });
    }

    [HttpGet("admin/all")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<List<NewsDto>>> GetAllForAdmin(CancellationToken ct)
    {
        var query = from n in _db.News.AsNoTracking()
                    join u in _db.Users on n.AuthorId equals u.Id
                    orderby n.CreatedAt descending
                    select new NewsDto(n.Id, n.Title, n.Body, n.ImageUrl, n.Published, n.CreatedAt, n.UpdatedAt, u.DisplayName, u.AvatarUrl);
        return Ok(await query.ToListAsync(ct));
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<NewsDto>> Create([FromBody] SaveNewsRequest req, CancellationToken ct)
    {
        var news = new NewsPost
        {
            Title = req.Title,
            Body = req.Body,
            ImageUrl = req.ImageUrl,
            Published = req.Published,
            AuthorId = User.GetUserId() ?? Guid.Empty
        };

        _db.News.Add(news);
        SyncImages(news);
        await _db.SaveChangesAsync(ct);
        return Ok();
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Update(Guid id, [FromBody] SaveNewsRequest req, CancellationToken ct)
    {
        var news = await _db.News.FindAsync(new object[] { id }, ct);
        if (news is null) return NotFound();

        news.Title = req.Title;
        news.Body = req.Body;
        news.ImageUrl = req.ImageUrl;
        news.Published = req.Published;
        news.UpdatedAt = DateTimeOffset.UtcNow;

        SyncImages(news);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var news = await _db.News.FindAsync(new object[] { id }, ct);
        if (news is null) return NotFound();

        // Каскадное удаление картинок из MinIO
        var linkedImages = await _db.NewsImages.Where(x => x.NewsPostId == id).ToListAsync(ct);
        foreach (var img in linkedImages)
        {
            await _storage.DeletePublicAsync(img.ObjectKey, ct);
        }

        _db.NewsImages.RemoveRange(linkedImages);
        _db.News.Remove(news);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>
    /// Вспомогательный метод парсинга ключей MinIO из текста и обложки
    /// </summary>
    private void SyncImages(NewsPost post)
    {
        var contentToSearch = $"{post.Body} {post.ImageUrl}";
        var matches = Regex.Matches(contentToSearch, @"(news/[a-zA-Z0-9-]+\.[a-zA-Z0-9]+)");

        var currentKeys = matches.Select(m => m.Value).Distinct().ToList();

        var existingLinks = _db.NewsImages.Where(x => x.NewsPostId == post.Id).ToList();
        _db.NewsImages.RemoveRange(existingLinks);

        foreach (var key in currentKeys)
        {
            _db.NewsImages.Add(new NewsImage { NewsPostId = post.Id, ObjectKey = key });
        }
    }
}