using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Cast.API.Events;

/// <summary>
/// Держатель единственного подключения к RabbitMQ (синглтон). Соединение
/// потокобезопасно; каналы (IModel) — нет, поэтому их создают по месту
/// </summary>
public sealed class RabbitMqConnection : IDisposable
{
    private readonly RabbitMqOptions _options;
    private readonly object _lock = new();
    private IConnection? _connection;

    public RabbitMqConnection(IOptions<RabbitMqOptions> options) => _options = options.Value;

    public RabbitMqOptions Options => _options;

    public IConnection GetConnection()
    {
        if (_connection is { IsOpen: true })
            return _connection;

        lock (_lock)
        {
            if (_connection is { IsOpen: true })
                return _connection;

            _connection?.Dispose();
            var factory = new ConnectionFactory
            {
                HostName = _options.Host,
                Port = _options.Port,
                UserName = _options.User,
                Password = _options.Password,
                DispatchConsumersAsync = true,
                AutomaticRecoveryEnabled = true
            };
            _connection = factory.CreateConnection("cast-api");
            return _connection;
        }
    }

    /// <summary>
    /// Создаёт канал и объявляет общий fanout-обменник событий
    /// </summary>
    public IModel CreateChannelWithExchange()
    {
        var channel = GetConnection().CreateModel();
        channel.ExchangeDeclare(_options.EventsExchange, ExchangeType.Fanout, durable: true, autoDelete: false);
        return channel;
    }

    public void Dispose() => _connection?.Dispose();
}
