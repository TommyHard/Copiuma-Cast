namespace Cast.API.Domain;

/// <summary>
/// Игра в каталоге платформы. Slug совпадает с gameId манифеста и
/// связывает карточку игры с набором доступных событий (ManifestCatalog) и с
/// комнатами (Room.GameId). Метаданные питают карточки и страницу игры
/// </summary>
public sealed class Game
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Машинный идентификатор игры (gameId манифеста), напр. "gta_sa". Уникален
    /// </summary>
    public string Slug { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Genre { get; set; }
    public string? BannerUrl { get; set; }
    public DateOnly? ReleaseDate { get; set; }
    public string? InteractionsJson { get; set; }
    public string? ModArchiveUrl { get; set; }
    public string? ModManifestJson { get; set; }

    /// <summary>
    /// Видна ли игра в каталоге (админ может скрыть)
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}