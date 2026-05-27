namespace Cast.API.Domain;

/// <summary>
/// Баланс зрителя, локализованный по конкретному стримеру: 
/// валюта не глобальна, а привязана к паре зритель+стример.
/// Кошелёк создаётся при первом взаимодействии со стартовым балансом и далее
/// пополняется фармингом watch-time, тратится на события и ставки
/// </summary>
public sealed class StreamerWallet
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Зритель — владелец баланса
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Стример, к которому привязан баланс (владелец комнаты)
    /// </summary>
    public Guid StreamerId { get; set; }

    public long Coins { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
