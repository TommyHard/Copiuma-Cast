namespace Cast.API.Domain;

/// <summary>
/// Журнал инициированных зрителями событий — аудит и основа для статистики,
/// антиспама и экономики
/// </summary>
public sealed class EventLogEntry
{
    public long Id { get; set; }

    public Guid RoomId { get; set; }
    public Guid UserId { get; set; }

    public string EventId { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// Параметры события (сериализованный JSON), если были
    /// </summary>
    public string? ArgsJson { get; set; }

    /// <summary>
    /// Списанная стоимость в виртуальной валюте
    /// </summary>
    public long CostCoins { get; set; }

    /// <summary>
    /// Прикреплённое к событию медиа (для популярности медиа)
    /// </summary>
    public Guid? MediaId { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}