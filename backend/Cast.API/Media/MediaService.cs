using Cast.API.Data;
using Cast.API.Domain;
using Cast.API.Storage;
using Cast.API.Tags;
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
    private readonly TagService _tags;

    public MediaService(CastDbContext db, StorageService storage, TagService tags)
    {
        _db = db;
        _storage = storage;
        _tags = tags;
    }

    public async Task<MediaDto> CreateAsync(Guid ownerId, string title, MediaType type, string originalKey,
        IEnumerable<string> tags, int? clipStartMs, int? clipEndMs, int posXPct, int posYPct, int scalePct,
        CancellationToken ct = default)
    {
        var item = new MediaItem
        {
            OwnerId = ownerId,
            Title = title.Trim(),
            Type = type,
            OriginalKey = originalKey,
            Processed = false,
            // Теги задаёт пользователь при загрузке; пополняем справочник
            Tags = await _tags.EnsureAsync(tags, ct),
            ClipStartMs = clipStartMs is > 0 ? clipStartMs : null,
            ClipEndMs = clipEndMs is > 0 ? clipEndMs : null,
            PosXPct = Math.Clamp(posXPct, 0, 100),
            PosYPct = Math.Clamp(posYPct, 0, 100),
            ScalePct = Math.Clamp(scalePct, 10, 400)
        };
        _db.MediaItems.Add(item);
        await _db.SaveChangesAsync(ct);
        return Map(item);
    }

    public async Task<List<MediaDto>> MyLibraryAsync(Guid ownerId, CancellationToken ct = default)
    {
        var items = await _db.MediaItems.AsNoTracking()
            .Where(m => m.OwnerId == ownerId)
            .OrderByDescending(m => m.CreatedAt)
            .ToListAsync(ct);
        return await MapManyAsync(items, ct);
    }

    /// <summary>
    /// Каталог всех одобренных медиа с поиском по названию, фильтром по типу и
    /// тегу и сортировкой (newest|title|cheap|expensive)
    /// </summary>
    public async Task<List<MediaDto>> CatalogAsync(string? search, MediaType? type, string? tag, string? sort, CancellationToken ct = default)
    {
        var q = _db.MediaItems.AsNoTracking().Where(m => m.Status == MediaStatus.Approved);

        if (type is not null)
            q = q.Where(m => m.Type == type);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            q = q.Where(m => m.Title.ToLower().Contains(s));
        }
        if (!string.IsNullOrWhiteSpace(tag))
        {
            var tg = tag.Trim().ToLower();
            q = q.Where(m => m.Tags.Contains(tg));
        }

        q = sort switch
        {
            "title" => q.OrderBy(m => m.Title),
            "cheap" => q.OrderBy(m => m.CostCoins),
            "expensive" => q.OrderByDescending(m => m.CostCoins),
            _ => q.OrderByDescending(m => m.CreatedAt)
        };

        var items = await q.Take(200).ToListAsync(ct);
        return await MapManyAsync(items, ct);
    }

    public async Task<MediaDto?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var item = await _db.MediaItems.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id, ct);
        if (item is null)
            return null;
        return (await MapManyAsync(new List<MediaItem> { item }, ct)).First();
    }

    public async Task<List<MediaDto>> PendingAsync(CancellationToken ct = default)
    {
        var items = await _db.MediaItems.AsNoTracking()
            .Where(m => m.Status == MediaStatus.Pending)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(ct);
        return await MapManyAsync(items, ct);
    }

    public async Task<bool> ApproveAsync(Guid adminId, Guid id, IEnumerable<string> tags, long costCoins, CancellationToken ct = default)
    {
        var item = await _db.MediaItems.FirstOrDefaultAsync(m => m.Id == id, ct);
        if (item is null)
            return false;
        item.Status = MediaStatus.Approved;
        item.Tags = await _tags.EnsureAsync(tags, ct);
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
    /// Все медиа для админ-управления (опц. фильтр по статусу), с именами
    /// </summary>
    public async Task<List<MediaDto>> AdminListAsync(MediaStatus? status, CancellationToken ct = default)
    {
        var q = _db.MediaItems.AsNoTracking().AsQueryable();
        if (status is not null)
            q = q.Where(m => m.Status == status);
        var items = await q.OrderByDescending(m => m.CreatedAt).Take(500).ToListAsync(ct);
        return await MapManyAsync(items, ct);
    }

    /// <summary>
    /// Приостановить доступ к медиа (нельзя использовать, но не удалено)
    /// </summary>
    public async Task<bool> SuspendAsync(Guid adminId, Guid id, CancellationToken ct = default)
    {
        var item = await _db.MediaItems.FirstOrDefaultAsync(m => m.Id == id, ct);
        if (item is null)
            return false;
        item.Status = MediaStatus.Suspended;
        item.ReviewedBy = adminId;
        item.ReviewedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// Снять приостановку: вернуть медиа в одобренные
    /// </summary>
    public async Task<bool> RestoreAsync(Guid adminId, Guid id, CancellationToken ct = default)
    {
        var item = await _db.MediaItems.FirstOrDefaultAsync(m => m.Id == id, ct);
        if (item is null)
            return false;
        item.Status = MediaStatus.Approved;
        item.ReviewedBy = adminId;
        item.ReviewedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// Админская правка тегов и стоимости (статус не меняется)
    /// </summary>
    public async Task<bool> AdminEditAsync(Guid id, IEnumerable<string> tags, long costCoins, CancellationToken ct = default)
    {
        var item = await _db.MediaItems.FirstOrDefaultAsync(m => m.Id == id, ct);
        if (item is null)
            return false;
        item.Tags = await _tags.EnsureAsync(tags, ct);
        item.CostCoins = costCoins < 0 ? 0 : costCoins;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// Полностью удалить медиа с сервиса: объекты из хранилища и запись из БД
    /// </summary>
    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var item = await _db.MediaItems.FirstOrDefaultAsync(m => m.Id == id, ct);
        if (item is null)
            return false;

        await _storage.DeleteMediaAsync(item.OriginalKey, ct);
        await _storage.DeleteMediaAsync(item.WebmKey, ct);
        await _storage.DeleteMediaAsync(item.OggKey, ct);

        _db.MediaItems.Remove(item);
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

    private MediaDto Map(MediaItem m, string? ownerName = null, string? approverName = null) => new(
        m.Id, m.OwnerId, m.Title, m.Type, m.Status, m.Tags, m.CostCoins,
        _storage.PresignMedia(m.OriginalKey),
        m.WebmKey is null ? null : _storage.PresignMedia(m.WebmKey),
        m.OggKey is null ? null : _storage.PresignMedia(m.OggKey),
        m.DurationMs, m.ClipStartMs, m.ClipEndMs, m.PosXPct, m.PosYPct, m.ScalePct,
        m.Processed, m.CreatedAt, ownerName, approverName, m.ReviewedAt);

    /// <summary>
    /// Маппинг с подстановкой отображаемых имён (кто загрузил / кто одобрил)
    /// одним запросом к пользователям
    /// </summary>
    private async Task<List<MediaDto>> MapManyAsync(List<MediaItem> items, CancellationToken ct)
    {
        var ids = items.Select(i => i.OwnerId)
            .Concat(items.Where(i => i.ReviewedBy.HasValue).Select(i => i.ReviewedBy!.Value))
            .Distinct()
            .ToList();

        var names = await _db.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);

        return items.Select(m => Map(
            m,
            names.GetValueOrDefault(m.OwnerId),
            m.ReviewedBy is { } rb ? names.GetValueOrDefault(rb) : null)).ToList();
    }

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