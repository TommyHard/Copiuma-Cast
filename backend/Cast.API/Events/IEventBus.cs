namespace Cast.API.Events;

/// <summary>
/// Публикация событий в очередь (RabbitMQ)
/// </summary>
public interface IEventBus
{
    Task PublishAsync(EventMessage message, CancellationToken ct = default);
}