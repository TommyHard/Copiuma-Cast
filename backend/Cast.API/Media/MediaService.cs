using Cast.API.Data;
using Cast.API.Domain;
using Cast.API.Storage;
using Cast.Shared.GameBridge;
using Microsoft.EntityFrameworkCore;

namespace Cast.API.Media;

/// <summary>
/// Медиатека: создание записей, выдача библиотеки пользователя и ручная
/// модерация. Файлы хранятся в объектном хранилище; 
/// здесь — метаданные и статусы
/// </summary>
public sealed class MediaService
{
    private readonly CastDbContext _db;
    private readonly StorageService _storage;

    public MediaService(CastDbContext db, StorageService storage)
    {
        _db = db;
        _storage = storage;
    }

    public async Task<MediaDto> CreateAsync(Guid ownerId, string title, MediaType type, string originalKey, CancellationToken ct = default)
    {
        var item = new MediaItem
        {
            OwnerId = ownerId,
            Title = title.Trim(),
            Type = type,
            OriginalKey = originalKey,
            Processed = false
        };
        _db.MediaItems.Add(item);
        await _db.SaveChangesAsync(ct);
        return Map(item);
    }

    public async Task<List<MediaDto>> MyLibraryAsync(Guid ownerId, CancellationToken ct = default)
        => (await _db.MediaItems.AsNoTracking()
            .Where(m => m.OwnerId == ownerId)
            .OrderByDescending(m => m.CreatedAt)
            .ToListAsync(ct))
            .Select(Map).ToList();

    public async Task<MediaDto?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var item = await _db.MediaItems.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id, ct);
        return item is null ? null : Map(item);
    }

    public async Task<List<MediaDto>> PendingAsync(CancellationToken ct = default)
        => (await _db.MediaItems.AsNoTracking()
            .Where(m => m.Status == MediaStatus.Pending)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(ct))
            .Select(Map).ToList();

    public async Task<bool> ApproveAsync(Guid adminId, Guid id, IEnumerable<string> tags, long costCoins, CancellationToken ct = default)
    {
        var item = await _db.MediaItems.FirstOrDefaultAsync(m => m.Id == id, ct);
        if (item is null)
            return false;
        item.Status = MediaStatus.Approved;
        item.Tags = tags.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()).Distinct().ToList();
        item.CostCoins = costCoins < 0 ? 0 : costCoins;
        item.ReviewedBy = adminId;
        item.ReviewedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> RejectAsync(Guid adminId, Guid id, CancellationToken ct = default)
    {
        var item = await _db.MediaItems.FirstOrDefaultAsync(m => m.Id == id, ct);
        if (item is null)
            return false;
        item.Status = MediaStatus.Rejected;
        item.ReviewedBy = adminId;
        item.ReviewedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// Готовит медиа к воспроизведению в комнате стримера: проверяет, что оно
    /// одобрено, обработано (для звука) и не нарушает tolerance-фильтры стримера.
    /// Возвращает (ok, причина-отказа, данные воспроизведения)
    /// </summary>
    public async Task<(bool ok, string? error, MediaPlayback? media, long cost)> ResolveForPlaybackAsync(
        Guid mediaId, Guid streamerId, CancellationToken ct = default)
    {
        var item = await _db.MediaItems.AsNoTracking().FirstOrDefaultAsync(m => m.Id == mediaId, ct);
        if (item is null)
            return (false, "Медиа не найдено.", null, 0);
        if (item.Status != MediaStatus.Approved)
            return (false, "Медиа не одобрено.", null, 0);
        if (item.Type == MediaType.Sound && !item.Processed)
            return (false, "Аудио ещё обрабатывается.", null, 0);

        // Tolerance-фильтр стримера с учётом режима (block-list / allow-list)
        var mode = (await _db.StreamerFilterSettings.FindAsync(new object?[] { streamerId }, ct))?.Mode
                   ?? FilterMode.Blocklist;
        var set = (await _db.StreamerTagFilters
            .Where(f => f.StreamerId == streamerId)
            .Select(f => f.Tag)
            .ToListAsync(ct)).ToHashSet();
        var itemTags = item.Tags.Select(t => t.ToLowerInvariant()).ToList();
        var blocked = mode == FilterMode.Blocklist
            ? itemTags.Any(set.Contains)            // запрещён хотя бы один тег
            : itemTags.Any(t => !set.Contains(t));  // есть тег вне разрешённого списка
        if (blocked)
            return (false, "Медиа заблокировано фильтрами стримера.", null, 0);

        var webm = item.Type == MediaType.Sound
            ? (item.WebmKey is null ? null : _storage.PresignMedia(item.WebmKey))
            : _storage.PresignMedia(item.OriginalKey);
        var ogg = item.Type == MediaType.Sound && item.OggKey is not null ? _storage.PresignMedia(item.OggKey) : null;
        var playback = new MediaPlayback
        {
            Title = item.Title,
            WebmUrl = webm,
            OggUrl = ogg,
            ClipStartMs = item.ClipStartMs,
            ClipEndMs = item.ClipEndMs,
            PosXPct = item.PosXPct,
            PosYPct = item.PosYPct,
            ScalePct = item.ScalePct,
        };
        return (true, null, playback, item.CostCoins);
    }

    private MediaDto Map(MediaItem m) => new(
        m.Id, m.OwnerId, m.Title, m.Type, m.Status, m.Tags, m.CostCoins,
        _storage.PresignMedia(m.OriginalKey),
        m.WebmKey is null ? null : _storage.PresignMedia(m.WebmKey),
        m.OggKey is null ? null : _storage.PresignMedia(m.OggKey),
        m.DurationMs, m.ClipStartMs, m.ClipEndMs, m.PosXPct, m.PosYPct, m.ScalePct,
        m.Processed, m.CreatedAt);

    /// <summary>
    /// Обновить монтаж/позицию (владелец)
    /// </summary>
    public async Task<bool> EditAsync(Guid ownerId, Guid id, EditMediaRequest req, CancellationToken ct = default)
    {
        var item = await _db.MediaItems.FirstOrDefaultAsync(m => m.Id == id && m.OwnerId == ownerId, ct);
        if (item is null)
            return false;

        item.ClipStartMs = req.ClipStartMs is > 0 ? req.ClipStartMs : null;
        item.ClipEndMs = req.ClipEndMs is > 0 ? req.ClipEndMs : null;
        item.PosXPct = Math.Clamp(req.PosXPct, 0, 100);
        item.PosYPct = Math.Clamp(req.PosYPct, 0, 100);
        item.ScalePct = Math.Clamp(req.ScalePct, 10, 400);

        item.Processed = false;
        item.ProcessingError = null;
        item.Status = MediaStatus.Pending;

        await _db.SaveChangesAsync(ct);
        return true;
    }
}