namespace Cast.API.Domain;

/// <summary>
/// Один из исходов ставки, на который зрители делают ставки
/// </summary>
public sealed class BetOutcome
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BetId { get; set; }
    public Bet? Bet { get; set; }

    public string Label { get; set; } = string.Empty;
}