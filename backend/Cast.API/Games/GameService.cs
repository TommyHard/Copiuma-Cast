using Cast.API.Data;
using Cast.Shared.GameBridge;
using Microsoft.EntityFrameworkCore;

namespace Cast.API.Games;

/// <summary>
/// Чтение каталога игр и расчёт статистики страницы игры. Статистика считается
/// из журнала событий (EventLog) и проводок (watch-time) с фильтром по комнатам
/// этой игры (Room.GameId == slug)
/// </summary>
public sealed class GameService
{
    private readonly CastDbContext _db;
    private readonly ManifestCatalog _catalog;

    public GameService(CastDbContext db, ManifestCatalog catalog)
    {
        _db = db;
        _catalog = catalog;
    }

    public Task<List<GameCardDto>> GetCatalogAsync(CancellationToken ct = default)
        // Возвращаем ВСЕ игры (включая выключенные) с флагом IsEnabled — UI
        // показывает их приглушёнными и не пускает внутрь, а не скрывает
        => _db.Games.AsNoTracking()
            .OrderBy(g => g.Title)
            .Select(g => new GameCardDto(g.Slug, g.Title, g.Description, g.Genre, g.BannerUrl, g.ReleaseDate, g.IsEnabled))
            .ToListAsync(ct);

    public async Task<GameDetailDto?> GetDetailAsync(string slug, Guid userId, bool isAdmin = false, CancellationToken ct = default)
    {
        var game = await _db.Games.AsNoTracking()
            .FirstOrDefaultAsync(g => g.Slug == slug && (g.IsEnabled || isAdmin), ct);

        if (game is null)
            return null;

        var card = new GameCardDto(game.Slug, game.Title, game.Description, game.Genre, game.BannerUrl, game.ReleaseDate, game.IsEnabled);

        IReadOnlyList<GameEventDefinition> interactions = Array.Empty<GameEventDefinition>();
        if (!string.IsNullOrWhiteSpace(game.InteractionsJson))
        {
            var options = new System.Text.Json.JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase) }
            };

            try
            {
                var manifest = System.Text.Json.JsonSerializer.Deserialize<Cast.Shared.GameBridge.GameManifest>(game.InteractionsJson, options);
                if (manifest?.Events != null)
                {
                    interactions = manifest.Events;
                }
            }
            catch (System.Text.Json.JsonException)
            {
                try
                {
                    interactions = System.Text.Json.JsonSerializer.Deserialize<List<GameEventDefinition>>(game.InteractionsJson, options)
                                   ?? new List<GameEventDefinition>();
                }
                catch { }
            }
        }
        else
        {
            try { interactions = _catalog.GetEvents(slug); } catch { }
        }

        var personal = await StatsAsync(slug, userId, ct);
        var global = await StatsAsync(slug, null, ct);

        var disabledEvents = await _db.GameEventOverrides
            .Where(o => o.GameId == slug && !o.Enabled)
            .Select(o => o.EventId)
            .ToListAsync(ct);

        return new GameDetailDto(card, interactions, personal, global, game.ModArchiveUrl, game.ModManifestJson, disabledEvents);
    }

    public async Task<GameDetailDto> UpsertAsync(string? currentSlug, GameUpsertDto dto, Storage.StorageService storage, CancellationToken ct)
    {
        var game = currentSlug != null
            ? await _db.Games.FirstOrDefaultAsync(g => g.Slug == currentSlug, ct)
            : await _db.Games.FirstOrDefaultAsync(g => g.Slug == dto.Slug, ct) ?? new Domain.Game();

        if (game == null)
            throw new Exception("Игра не найдена");

        game.Title = dto.Title;
        game.Slug = dto.Slug;
        game.Genre = dto.Genre;
        game.Description = dto.Description;
        game.IsEnabled = dto.IsEnabled;

        if (dto.InteractionsJson != null) game.InteractionsJson = dto.InteractionsJson;
        if (dto.ModManifestJson != null) game.ModManifestJson = dto.ModManifestJson;

        if (dto.Banner != null)
        {
            using var stream = dto.Banner.OpenReadStream();
            game.BannerUrl = await storage.UploadPublicAsync(stream, $"games/banners/{dto.Slug}-{Guid.NewGuid()}.png", dto.Banner.ContentType, ct);
        }

        if (dto.ModArchive != null)
        {
            using var stream = dto.ModArchive.OpenReadStream();
            game.ModArchiveUrl = await storage.UploadPublicAsync(stream, $"games/mods/{dto.Slug}-{Guid.NewGuid()}.zip", dto.ModArchive.ContentType, ct);
        }

        if (_db.Entry(game).State == EntityState.Detached)
        {
            _db.Games.Add(game);
        }

        await _db.SaveChangesAsync(ct);

        var detail = await GetDetailAsync(game.Slug, Guid.Empty, isAdmin: true, ct);

        if (detail == null)
        {
            var card = new GameCardDto(game.Slug, game.Title, game.Description, game.Genre, game.BannerUrl, game.ReleaseDate, game.IsEnabled);
            return new GameDetailDto(card, new List<GameEventDefinition>(), new GameStatsDto(0, 0, 0, 0), new GameStatsDto(0, 0, 0, 0), game.ModArchiveUrl, game.ModManifestJson, Array.Empty<string>());
        }

        return detail;
    }

    public async Task DeleteAsync(string slug, CancellationToken ct)
    {
        var game = await _db.Games.FirstOrDefaultAsync(g => g.Slug == slug, ct);
        if (game != null)
        {
            _db.Games.Remove(game);
            await _db.SaveChangesAsync(ct);
        }
    }

    public async Task<GameEventDefinition?> GetEventDefinitionAsync(string slug, string eventId, CancellationToken ct = default)
    {
        var detail = await GetDetailAsync(slug, Guid.Empty, isAdmin: true, ct);
        if (detail == null) return null;

        return detail.Interactions.FirstOrDefault(e => e.Id.Equals(eventId, StringComparison.OrdinalIgnoreCase) && e.Enabled);
    }

    public async Task ToggleAsync(string slug, CancellationToken ct)
    {
        var game = await _db.Games.FirstOrDefaultAsync(g => g.Slug == slug, ct);
        if (game != null)
        {
            game.IsEnabled = !game.IsEnabled;
            await _db.SaveChangesAsync(ct);
        }
    }

    /// <summary>
    /// Статистика по игре. userId == null — глобальная, иначе личная
    /// </summary>
    private async Task<GameStatsDto> StatsAsync(string slug, Guid? userId, CancellationToken ct)
    {
        var roomIds = _db.Rooms.Where(r => r.GameId == slug).Select(r => r.Id);

        var events = _db.EventLog.Where(e => roomIds.Contains(e.RoomId));
        if (userId is not null)
            events = events.Where(e => e.UserId == userId);

        var eventsTriggered = await events.LongCountAsync(ct);
        var pointsSpent = await events.SumAsync(e => (long?)e.CostCoins, ct) ?? 0;

        int sessions = userId is null
            ? await _db.Rooms.CountAsync(r => r.GameId == slug, ct)
            : await _db.RoomMemberships.CountAsync(m => m.UserId == userId && roomIds.Contains(m.RoomId), ct);

        // Часы просмотра: каждая проводка watch-time = одна минута присутствия
        var watch = _db.CoinTransactions.Where(t => t.EventId == "watchtime" && roomIds.Contains(t.RoomId));
        if (userId is not null)
            watch = watch.Where(t => t.UserId == userId);
        var watchMinutes = await watch.LongCountAsync(ct);

        return new GameStatsDto(sessions, eventsTriggered, pointsSpent, Math.Round(watchMinutes / 60.0, 1));
    }
}