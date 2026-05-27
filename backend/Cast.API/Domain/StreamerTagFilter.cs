namespace Cast.API.Domain;

/// <summary>
/// Tolerance-фильтр стримера: тег, который стример заблокировал. Медиа,
/// содержащее любой из заблокированных тегов, нельзя воспроизвести в комнатах
/// этого стримера. Хранится по одной строке на тег
/// </summary>
public sealed class StreamerTagFilter
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid StreamerId { get; set; }

    /// <summary>
    /// Заблокированный тег (нижний регистр)
    /// </summary>
    public string Tag { get; set; } = string.Empty;
}