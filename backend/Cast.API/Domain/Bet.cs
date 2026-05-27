namespace Cast.API.Domain;

public enum BetStatus
{
    /// <summary>
    /// Приём ставок открыт (до LocksAt)
    /// </summary>
    Open = 0,
    /// <summary>
    /// Разрешена, выигрыши распределены
    /// </summary>
    Resolved = 1,
    /// <summary>
    /// Отменена, ставки возвращены
    /// </summary>
    Cancelled = 2
}

/// <summary>
/// Ставка (тотализатор) в комнате: стример задаёт вопрос, исходы и время приёма.
/// Модель паримьютюэль — коэффициенты считаются от распределения пула по исходам
/// </summary>
public sealed class Bet
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid RoomId { get; set; }
    public Guid StreamerId { get; set; }

    public string Title { get; set; } = string.Empty;

    public BetStatus Status { get; set; } = BetStatus.Open;

    /// <summary>
    /// Время закрытия приёма ставок
    /// </summary>
    public DateTimeOffset LocksAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ResolvedAt { get; set; }

    public Guid? WinningOutcomeId { get; set; }

    public ICollection<BetOutcome> Outcomes { get; set; } = new List<BetOutcome>();
    public ICollection<BetWager> Wagers { get; set; } = new List<BetWager>();
}