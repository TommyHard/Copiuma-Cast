namespace Cast.API.Domain;

/// <summary>
/// Подписка зрителя на стримера (однонаправленная, в отличие от дружбы)
/// </summary>
public sealed class Follow
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid FollowerId { get; set; }
    public Guid StreamerId { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}