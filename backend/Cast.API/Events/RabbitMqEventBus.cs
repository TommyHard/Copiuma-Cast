using System.Text.Json;
using RabbitMQ.Client;

namespace Cast.API.Events;

/// <summary>
/// Издатель событий в RabbitMQ. Сериализует <see cref="EventMessage"/> в JSON и
/// публикует в fanout-обменник. Канал создаётся на публикацию (IModel не
/// потокобезопасен) — для текущей нагрузки достаточно
/// </summary>
public sealed class RabbitMqEventBus : IEventBus
{
    private readonly RabbitMqConnection _connection;

    public RabbitMqEventBus(RabbitMqConnection connection) => _connection = connection;

    public Task PublishAsync(EventMessage message, CancellationToken ct = default)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(message);

        using var channel = _connection.CreateChannelWithExchange();
        var props = channel.CreateBasicProperties();
        props.ContentType = "application/json";
        props.DeliveryMode = 2;

        channel.BasicPublish(
            exchange: _connection.Options.EventsExchange,
            routingKey: string.Empty,
            basicProperties: props,
            body: body);

        return Task.CompletedTask;
    }
}
