namespace Cast.API.Events;

/// <summary>
/// Сообщение о событии, инициированном зрителем. Публикуется в RabbitMQ хабом,
/// обрабатывается консьюмером (списание валюты, журнал, антиспам) и доставляется
/// стримеру по SignalR, а его десктоп передаёт команду моду через GameBridge
/// </summary>
public sealed class EventMessage
{
    public Guid RoomId { get; set; }
    public string RoomCode { get; set; } = string.Empty;
    public Guid UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string EventId { get; set; } = string.Empty;
    public Dictionary<string, string> Args { get; set; } = new();
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
