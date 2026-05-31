namespace Cast.API.Domain;

public enum RoomRole
{
    Viewer = 0,
    Streamer = 1
}

/// <summary>
/// Связь пользователь<>комната с ролью. Один стример-владелец и много зрителей
/// </summary>
public sealed class RoomMembership
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid RoomId { get; set; }
    public Room? Room { get; set; }

    public Guid UserId { get; set; }
    public ApplicationUser? User { get; set; }

    public RoomRole Role { get; set; } = RoomRole.Viewer;

    /// <summary>
    /// Заблокирован стримером: не может войти/взаимодействовать в комнате
    /// </summary>
    public bool Banned { get; set; }

    /// <summary>
    /// Приглашение ещё не принято. true — создано стримером через "Пригласить";
    /// становится false, когда зритель входит в комнату. Отклонение удаляет запись
    /// </summary>
    public bool Pending { get; set; }

    /// <summary>
    /// Когда отправлено приглашение (для срока жизни приглашения)
    /// </summary>
    public DateTimeOffset? InvitedAt { get; set; }

    public DateTimeOffset JoinedAt { get; set; } = DateTimeOffset.UtcNow;
}