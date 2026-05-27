namespace Cast.API.Domain;

/// <summary>
/// Активное присутствие в комнате: одна строка на SignalR-соединение. Служит
/// двум целям: начисление watch-time активным зрителям и детект ухода стримера
/// (Dead Man's Switch). Строка создаётся при входе в комнату и удаляется при
/// разрыве соединения
/// </summary>
public sealed class RoomConnection
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid RoomId { get; set; }
    public Guid UserId { get; set; }
    public RoomRole Role { get; set; }

    /// <summary>
    /// Идентификатор SignalR-соединения (уникален)
    /// </summary>
    public string ConnectionId { get; set; } = string.Empty;

    public DateTimeOffset ConnectedAt { get; set; } = DateTimeOffset.UtcNow;
}