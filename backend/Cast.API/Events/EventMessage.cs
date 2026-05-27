namespace Cast.API.Events;

/// <summary>
/// Сообщение о событии, инициированном зрителем. Публикуется в RabbitMQ хабом
/// ПОСЛЕ авторитетной валидации, списания валюты и прямой доставки команды
/// стримеру — то есть вне горячего пути. Консьюмер использует его только для
/// журнала/аналитики/антиспама, не для доставки
/// </summary>
public sealed class EventMessage
{
    public Guid RoomId { get; set; }
    public string RoomCode { get; set; } = string.Empty;
    public Guid UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string EventId { get; set; } = string.Empty;
    public Dictionary<string, string> Args { get; set; } = new();

    /// <summary>
    /// Списанная стоимость события (фиксируется в журнале консьюмером)
    /// </summary>
    public long CostCoins { get; set; }

    /// <summary>
    /// Прикреплённое медиа (для статистики), если было
    /// </summary>
    public Guid? MediaId { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}