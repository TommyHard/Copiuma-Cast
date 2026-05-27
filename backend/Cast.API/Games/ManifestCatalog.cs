using Cast.Shared.GameBridge;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cast.API.Games;

/// <summary>
/// Серверный каталог манифестов игр — авторитетный источник доступных событий.
/// На старте читает JSON-манифесты из каталога (по одному на игру), ключ —
/// gameId. Поиск события идёт по памяти, без обращения к БД, чтобы не добавлять
/// задержку в горячий путь. Позже источником станет БД, 
/// но интерфейс <see cref="TryGetEnabledEvent"/> при этом не изменится
/// </summary>
public sealed class ManifestCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly Dictionary<string, GameManifest> _byGameId =
        new(StringComparer.OrdinalIgnoreCase);

    public ManifestCatalog(string directory, ILogger<ManifestCatalog> logger)
    {
        if (!Directory.Exists(directory))
        {
            logger.LogWarning("Каталог манифестов не найден: {Dir}", directory);
            return;
        }

        foreach (var path in Directory.EnumerateFiles(directory, "*.json"))
        {
            try
            {
                var manifest = JsonSerializer.Deserialize<GameManifest>(File.ReadAllText(path), JsonOptions);
                if (manifest is null || string.IsNullOrWhiteSpace(manifest.GameId))
                {
                    logger.LogWarning("Манифест без gameId пропущен: {Path}.", path);
                    continue;
                }
                _byGameId[manifest.GameId] = manifest;
                logger.LogInformation("Манифест загружен: {GameId} ({Count} событий).",
                    manifest.GameId, manifest.Events.Count);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Не удалось прочитать манифест {Path}.", path);
            }
        }
    }

    /// <summary>
    /// Все события (белый список) игры для карточки/страницы игры. Пусто, если
    /// игра неизвестна каталогу манифестов
    /// </summary>
    public IReadOnlyList<GameEventDefinition> GetEvents(string? gameId)
        => !string.IsNullOrWhiteSpace(gameId) && _byGameId.TryGetValue(gameId, out var manifest)
            ? manifest.Events
            : Array.Empty<GameEventDefinition>();

    /// <summary>
    /// Возвращает определение события из манифеста игры. false — если игра
    /// неизвестна, события нет в белом списке или оно выключено
    /// </summary>
    public bool TryGetEnabledEvent(string? gameId, string eventId, out GameEventDefinition def)
    {
        def = default!;
        if (string.IsNullOrWhiteSpace(gameId) || !_byGameId.TryGetValue(gameId, out var manifest))
            return false;

        var found = manifest.Events.FirstOrDefault(e =>
            string.Equals(e.Id, eventId, StringComparison.OrdinalIgnoreCase));
        if (found is null || !found.Enabled)
            return false;

        def = found;
        return true;
    }
}