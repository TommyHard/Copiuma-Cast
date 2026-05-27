namespace Cast.API.Domain;

public enum FriendStatus
{
    /// <summary>
    /// Запрос отправлен, ожидает ответа
    /// </summary>
    Pending = 0,
    /// <summary>
    /// Дружба подтверждена
    /// </summary>
    Accepted = 1
}

/// <summary>
/// Связь дружбы между пользователями. Хранится одной строкой: инициатор ->
/// адресат. Друзья — это принятые связи в любом из направлений
/// </summary>
public sealed class FriendLink
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid RequesterId { get; set; }
    public Guid AddresseeId { get; set; }

    public FriendStatus Status { get; set; } = FriendStatus.Pending;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? RespondedAt { get; set; }
}