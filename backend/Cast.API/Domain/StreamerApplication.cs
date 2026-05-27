namespace Cast.API.Domain;

public enum ApplicationStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2
}

/// <summary>
/// Заявка пользователя на статус стримера. Админ рассматривает её
/// вручную; при одобрении пользователю выдаётся роль Streamer
/// </summary>
public sealed class StreamerApplication
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    /// <summary>
    /// Сопроводительное сообщение от заявителя
    /// </summary>
    public string? Message { get; set; }

    public ApplicationStatus Status { get; set; } = ApplicationStatus.Pending;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid? ReviewedBy { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
}