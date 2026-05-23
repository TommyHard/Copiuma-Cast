using System.Text;
using System.Text.Json;
using Cast.API.Data;
using Cast.API.Domain;
using Cast.API.Realtime;
using Cast.Shared.GameBridge;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Cast.API.Events;

/// <summary>
/// Фоновый консьюмер: читает события из RabbitMQ, журналирует их и доставляет
/// стримеру по SignalR в виде <see cref="GameCommand"/>. Десктоп стримера
/// получает команду и передаёт моду через GameBridge.
///
/// Очередь развязывает приём от доставки: всплески событий зрителей не нагружают
/// хаб напрямую, можно масштабировать консьюмеров и переживать перезапуски
/// </summary>
public sealed class EventConsumerService : BackgroundService
{
    private readonly RabbitMqConnection _connection;
    private readonly RabbitMqOptions _options;
    private readonly IServiceProvider _services;
    private readonly IHubContext<RoomHub> _hub;
    private readonly ILogger<EventConsumerService> _logger;

    private IModel? _channel;

    public EventConsumerService(
        RabbitMqConnection connection,
        IOptions<RabbitMqOptions> options,
        IServiceProvider services,
        IHubContext<RoomHub> hub,
        ILogger<EventConsumerService> logger)
    {
        _connection = connection;
        _options = options.Value;
        _services = services;
        _hub = hub;
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
            _logger.LogError(ex, "Ошибка обработки события.");
            _channel?.BasicAck(ea.DeliveryTag, multiple: false);
        }
    }

    private async Task ProcessAsync(EventMessage msg)
    {
        using (var scope = _services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CastDbContext>();
            db.EventLog.Add(new EventLogEntry
            {
                RoomId = msg.RoomId,
                UserId = msg.UserId,
                EventId = msg.EventId,
                Username = msg.Username,
                ArgsJson = msg.Args.Count > 0 ? JsonSerializer.Serialize(msg.Args) : null,
                CostCoins = 0
            });
            await db.SaveChangesAsync();
        }

        var command = new GameCommand(msg.EventId, msg.Username) { Args = msg.Args };
        await _hub.Clients
            .Group(RoomHub.StreamerGroup(msg.RoomId))
            .SendAsync("GameCommand", command);
    }

    public override void Dispose()
    {
        _channel?.Dispose();
        base.Dispose();
    }
}