namespace Cast.API.Domain;

/// <summary>
/// Платформенное присутствие: одна строка на активное соединение клиента с
/// PresenceHub (вкладка/приложение открыто). В отличие от RoomConnection не
/// привязано к комнате — нужно, чтобы знать, что пользователь онлайн вне комнат
/// </summary>
public sealed class UserConnection
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    /// <summary>
    /// Идентификатор SignalR-соединения (уникален)
    /// </summary>
    public string ConnectionId { get; set; } = string.Empty;

    public DateTimeOffset ConnectedAt { get; set; } = DateTimeOffset.UtcNow;
}