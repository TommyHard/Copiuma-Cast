using Cast.API.Data;
using Cast.API.Domain;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;
using System.Text.Json;

namespace Cast.API.Events;

/// <summary>
/// Фоновый консьюмер журнала. Доставка команд стримеру происходит напрямую в
/// <see cref="Realtime.RoomHub"/> (горячий путь), поэтому здесь остаётся только
/// то, что можно делать асинхронно: запись в журнал событий, а в дальнейшем —
/// аналитика и антиспам. Очередь развязывает приём от обработки: всплески
/// событий не нагружают БД синхронно, обработку можно масштабировать и
/// переживать перезапуски
/// </summary>
public sealed class EventConsumerService : BackgroundService
{
    private readonly RabbitMqConnection _connection;
    private readonly RabbitMqOptions _options;
    private readonly IServiceProvider _services;
    private readonly ILogger<EventConsumerService> _logger;

    private IModel? _channel;

    public EventConsumerService(
        RabbitMqConnection connection,
        IOptions<RabbitMqOptions> options,
        IServiceProvider services,
        ILogger<EventConsumerService> logger)
    {
        _connection = connection;
        _options = options.Value;
        _services = services;
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _ = Task.Run(() => ConnectWithRetryAsync(stoppingToken), stoppingToken);
        return Task.CompletedTask;
    }

    private async Task ConnectWithRetryAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                StartConsuming();
                _logger.LogInformation("EventConsumer подключён к RabbitMQ.");
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "RabbitMQ недоступен, повтор через 5 с.");
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
            }
        }
    }

    private void StartConsuming()
    {
        var channel = _connection.CreateChannelWithExchange();
        channel.QueueDeclare(_options.DeliveryQueue, durable: true, exclusive: false, autoDelete: false);
        channel.QueueBind(_options.DeliveryQueue, _options.EventsExchange, routingKey: string.Empty);
        channel.BasicQos(0, prefetchCount: 16, global: false);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.Received += OnReceivedAsync;
        channel.BasicConsume(_options.DeliveryQueue, autoAck: false, consumer);
        _channel = channel;
    }

    private async Task OnReceivedAsync(object sender, BasicDeliverEventArgs ea)
    {
        try
        {
            var json = Encoding.UTF8.GetString(ea.Body.Span);
            var msg = JsonSerializer.Deserialize<EventMessage>(json);
            if (msg is not null)
                await ProcessAsync(msg);

            _channel?.BasicAck(ea.DeliveryTag, multiple: false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка обработки события журнала.");
            _channel?.BasicAck(ea.DeliveryTag, multiple: false);
        }
    }

    private async Task ProcessAsync(EventMessage msg)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CastDbContext>();
        db.EventLog.Add(new EventLogEntry
        {
            RoomId = msg.RoomId,
            UserId = msg.UserId,
            EventId = msg.EventId,
            Username = msg.Username,
            ArgsJson = msg.Args.Count > 0 ? JsonSerializer.Serialize(msg.Args) : null,
            CostCoins = msg.CostCoins,
            MediaId = msg.MediaId
        });
        await db.SaveChangesAsync();
    }

    public override void Dispose()
    {
        _channel?.Dispose();
        base.Dispose();
    }
}