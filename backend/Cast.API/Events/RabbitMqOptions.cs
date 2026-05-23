namespace Cast.API.Events;

public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string User { get; set; } = "guest";
    public string Password { get; set; } = "guest";

    /// <summary>
    /// Fanout-обменник для событий зрителей
    /// </summary>
    public string EventsExchange { get; set; } = "cast.events";

    /// <summary>
    /// Имя очереди консьюмера-доставщика
    /// </summary>
    public string DeliveryQueue { get; set; } = "cast.events.delivery";
}
