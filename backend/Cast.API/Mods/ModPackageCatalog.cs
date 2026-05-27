using System.Text.Json;

namespace Cast.API.Mods;

/// <summary>
/// Каталог пакетов модов для десктоп-клиента. Читает JSON-описания пакетов
/// из директории ModPackages при старте. Каждый JSON — один пакет (gameId -> файлы)
///
/// Формат файла (пример ModPackages/gta_sa.json):
/// {
///   "gameId": "gta_sa",
///   "gameName": "GTA San Andreas — Flame Story",
///   "modVersion": "1.0.0",
///   "description": "Мод для интерактивного стрима",
///   "libraries": [
///     { "relativePath": "moonloader/lib/samp/events.lua", "sha256": "abc...", "size": 1234 }
///   ],
///   "modFiles": [
///     { "relativePath": "moonloader/cast_bridge.lua", "sha256": "def...", "size": 5678 }
///   ],
///   "manifest": { "relativePath": "moonloader/manifest.json", "sha256": "ghi...", "size": 900 }
/// }
///
/// URL для скачивания строится автоматически: /api/mods/files/{gameId}/{relativePath}
/// </summary>
public sealed class ModPackageCatalog
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly Dictionary<string, ModPackageDto> _packages = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _filesRoot;

    public ModPackageCatalog(string packagesDir, string filesRoot, ILogger<ModPackageCatalog> logger)
    {
        _filesRoot = filesRoot;

        if (!Directory.Exists(packagesDir))
        {
            logger.LogWarning("Каталог пакетов модов не найден: {Dir}. Mod Manager будет пуст.", packagesDir);
            return;
        }

        foreach (var path in Directory.EnumerateFiles(packagesDir, "*.json"))
        {
            try
            {
                var raw = JsonSerializer.Deserialize<RawPackage>(File.ReadAllText(path), JsonOpts);
                if (raw is null || string.IsNullOrWhiteSpace(raw.GameId)) continue;

                var pkg = new ModPackageDto
                {
                    GameId = raw.GameId,
                    GameName = raw.GameName ?? raw.GameId,
                    ModVersion = raw.ModVersion ?? "0.0.0",
                    Description = raw.Description,
                    Libraries = raw.Libraries?.Select(f => ToDto(raw.GameId, f)).ToList() ?? new(),
                    ModFiles = raw.ModFiles?.Select(f => ToDto(raw.GameId, f)).ToList() ?? new(),
                    Manifest = raw.Manifest is not null ? ToDto(raw.GameId, raw.Manifest) : null
                };

                _packages[raw.GameId] = pkg;
                logger.LogInformation("Пакет мода загружен: {GameId} v{Ver} ({Libs} libs + {Mods} mod files).",
                    raw.GameId, pkg.ModVersion, pkg.Libraries.Count, pkg.ModFiles.Count);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Ошибка чтения пакета мода {Path}.", path);
            }
        }
    }

    public IReadOnlyList<ModPackageDto> GetAll() => _packages.Values.ToList();

    public ModPackageDto? Get(string gameId)
        => _packages.GetValueOrDefault(gameId);

    /// <summary>
    /// Абсолютный путь к файлу мода на диске (для отдачи контроллером)
    /// </summary>
    public string? ResolveFilePath(string gameId, string relativePath)
    {
        var full = Path.GetFullPath(Path.Combine(_filesRoot, gameId, relativePath));
        var root = Path.GetFullPath(Path.Combine(_filesRoot, gameId));
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            return null;
        return File.Exists(full) ? full : null;
    }

    private static ModFileDto ToDto(string gameId, RawFile f) => new()
    {
        RelativePath = f.RelativePath,
        DownloadUrl = $"/api/mods/files/{gameId}/{f.RelativePath}",
        Sha256 = f.Sha256 ?? string.Empty,
        Size = f.Size
    };

    // --- Десериализация сырого JSON ---
    private sealed class RawPackage
    {
        public string GameId { get; set; } = string.Empty;
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