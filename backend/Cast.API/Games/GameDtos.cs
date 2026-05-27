using Cast.Shared.GameBridge;

namespace Cast.API.Games;

/// <summary>
/// Карточка игры в каталоге
/// </summary>
public sealed record GameCardDto(
    string Slug,
    string Title,
    string? Description,
    string? Genre,
    string? BannerUrl,
    DateOnly? ReleaseDate,
    bool IsEnabled);

/// <summary>
/// Агрегаты по игре: число сессий, инициированных событий, потраченных очков и
/// часов просмотра. Используется и для личной, и для глобальной статистики
/// </summary>
public sealed record GameStatsDto(int Sessions, long EventsTriggered, long PointsSpent, double WatchHours);

/// <summary>
/// Страница игры: карточка, доступные взаимодействия и статистика
/// </summary>
public sealed record GameDetailDto(
    GameCardDto Game,
    IReadOnlyList<GameEventDefinition> Interactions,
    GameStatsDto Personal,
    GameStatsDto Global,
    string? ModArchiveUrl,
    string? ModManifestJson,
    IReadOnlyList<string> GloballyDisabledEventIds);

public sealed class GameUpsertDto
{
    public string Title { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Genre { get; set; }
    public string? Description { get; set; }
    public bool IsEnabled { get; set; } = true;
    public IFormFile? Banner { get; set; }
    public string? InteractionsJson { get; set; }
    public IFormFile? ModArchive { get; set; }
    public string? ModManifestJson { get; set; }
}