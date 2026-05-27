namespace Cast.API.Domain;

/// <summary>
/// Комната стрима. Зрители подключаются по короткому коду. С комнатой связан
/// идентификатор игры (gameId из манифеста мода), чтобы знать набор доступных
/// событий
/// </summary>
public sealed class Room
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Короткий код для входа зрителей (уникальный)
    /// </summary>
    public string Code { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Владелец комнаты (стример)
    /// </summary>
    public Guid OwnerId { get; set; }
    public ApplicationUser? Owner { get; set; }

    /// <summary>
    /// Идентификатор игры (gameId манифеста)
    /// </summary>
    public string? GameId { get; set; }

    public bool IsOpen { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<RoomMembership> Memberships { get; set; } = new List<RoomMembership>();
}