namespace Cast.API.Domain;

/// <summary>
/// Глобальное переопределение доступности конкретного события игры
/// администратором: можно выключить отдельную функцию внутри игры, не
/// выключая игру целиком. Отсутствие строки = берётся значение из манифеста
/// </summary>
public sealed class GameEventOverride
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Slug игры (gameId манифеста)
    /// </summary>
    public string GameId { get; set; } = string.Empty;
    public string EventId { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;
}