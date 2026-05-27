namespace Cast.API.Domain;

public enum WagerStatus
{
    Placed = 0,
    Won = 1,
    Lost = 2,
    Refunded = 3
}

/// <summary>
/// Ставка зрителя на конкретный исход. Id служит идемпотентным ключом списания
/// при приёме (одно списание на ставку); выплата/возврат используют
/// производные детерминированные ключи
/// </summary>
public sealed class BetWager
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid BetId { get; set; }
    public Bet? Bet { get; set; }

    public Guid OutcomeId { get; set; }
    public Guid UserId { get; set; }
    public Guid StreamerId { get; set; }

    public long Amount { get; set; }
    public WagerStatus Status { get; set; } = WagerStatus.Placed;
    public long Payout { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}