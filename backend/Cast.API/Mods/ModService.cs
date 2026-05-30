using System.Text.Json;
using Cast.API.Data;
using Microsoft.EntityFrameworkCore;

namespace Cast.API.Mods;

/// <summary>
/// Источник пакетов модов для десктопного Mod Manager — БД игр (таблица Games).
/// Метаданные и список файлов берутся из админского манифеста установки
/// (Game.ModManifestJson, формат как у старого ModPackageCatalog), сам мод —
/// единым .zip по Game.ModArchiveUrl. Десктоп скачивает архив целиком и
/// распаковывает у себя
/// </summary>
public sealed class ModService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly CastDbContext _db;

    public ModService(CastDbContext db) => _db = db;

    /// <summary>
    /// Все игры, у которых загружен мод (есть .zip-архив)
    /// </summary>
    public async Task<List<ModPackageDto>> GetAllAsync(CancellationToken ct = default)
    {
        var games = await _db.Games.AsNoTracking()
            .Where(g => g.ModArchiveUrl != null)
            .OrderBy(g => g.Title)
            .Select(g => new { g.Slug, g.Title, g.ModArchiveUrl, g.ModManifestJson })
            .ToListAsync(ct);

        return games.Select(g => Build(g.Slug, g.Title, g.ModArchiveUrl, g.ModManifestJson)).ToList();
    }

    /// <summary>
    /// Пакет мода конкретной игры (null — если игры нет или мод не загружен)
    /// </summary>
    public async Task<ModPackageDto?> GetAsync(string gameId, CancellationToken ct = default)
    {
        var g = await _db.Games.AsNoTracking()
            .Where(x => x.Slug == gameId && x.ModArchiveUrl != null)
            .Select(x => new { x.Slug, x.Title, x.ModArchiveUrl, x.ModManifestJson })
            .FirstOrDefaultAsync(ct);

        return g is null ? null : Build(g.Slug, g.Title, g.ModArchiveUrl, g.ModManifestJson);
    }

    /// <summary>
    /// Собрать DTO из метаданных игры и манифеста установки
    /// </summary>
    private static ModPackageDto Build(string slug, string title, string? archiveUrl, string? manifestJson)
    {
        RawPackage? raw = null;
        if (!string.IsNullOrWhiteSpace(manifestJson))
        {
            try { raw = JsonSerializer.Deserialize<RawPackage>(manifestJson, JsonOpts); }
            catch (JsonException) { /* битый манифест — отдаём пакет без списка файлов */ }
        }

        return new ModPackageDto
        {
            GameId = slug,
            GameName = string.IsNullOrWhiteSpace(raw?.GameName) ? title : raw!.GameName!,
            ModVersion = raw?.ModVersion ?? "0.0.0",
            Description = raw?.Description,
            ArchiveUrl = archiveUrl ?? string.Empty,
            Libraries = raw?.Libraries?.Select(ToDto).ToList() ?? new(),
            ModFiles = raw?.ModFiles?.Select(ToDto).ToList() ?? new(),
            Manifest = raw?.Manifest is not null ? ToDto(raw.Manifest) : null
        };
    }

    private static ModFileDto ToDto(RawFile f) => new()
    {
        RelativePath = f.RelativePath,
        Sha256 = f.Sha256 ?? string.Empty,
        Size = f.Size
    };

    // --- Десериализация манифеста установки (Game.ModManifestJson) ---
    private sealed class RawPackage
    {
        public string? GameName { get; set; }
        public string? ModVersion { get; set; }
        public string? Description { get; set; }
        public List<RawFile>? Libraries { get; set; }
        public List<RawFile>? ModFiles { get; set; }
        public RawFile? Manifest { get; set; }
    }

    private sealed class RawFile
    {
        public string RelativePath { get; set; } = string.Empty;
        public string? Sha256 { get; set; }
        public long Size { get; set; }
    }
}
