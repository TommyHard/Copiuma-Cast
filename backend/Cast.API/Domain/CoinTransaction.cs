namespace Cast.API.Domain;

/// <summary>
/// Финансовая проводка по виртуальной валюте — авторитетный журнал движения
/// средств. Главный ключ <see cref="Id"/> служит идемпотентным ключом операции:
/// повторная попытка с тем же ключом не списывает повторно (защита от
/// дублей при ретраях). Отрицательная сумма — списание (вызов события зрителем),
/// положительная — начисление (фарминг watch-time, возврат ставки)
/// </summary>
public sealed class CoinTransaction
{
    /// <summary>
    /// Идемпотентный ключ операции (генерируется при инициировании события)
    /// </summary>
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    /// <summary>
    /// Стример, к чьему балансу относится проводка
    /// </summary>
    public Guid StreamerId { get; set; }

    public Guid RoomId { get; set; }

    /// <summary>
    /// Событие, за которое прошла проводка (для списаний за вызов события)
    /// </summary>
    public string EventId { get; set; } = string.Empty;

    /// <summary>
    /// Изменение баланса: отрицательное — списание, положительное — начисление
    /// </summary>
    public long Amount { get; set; }

    /// <summary>
    /// Баланс пользователя после применения проводки (снимок для аудита)
    /// </summary>
    public long BalanceAfter { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}