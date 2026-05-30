namespace Cast.Shared.GameBridge;

/// <summary>
/// Один участник комнаты в ростере. Несколько соединений одного пользователя
/// схлопываются в одну запись
/// </summary>
public sealed class RoomMember
{
    public string UserId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Роль в комнате: "Viewer" или "Streamer"
    /// </summary>
    public string Role { get; set; } = string.Empty;

    public RoomMember() { }

    public RoomMember(string userId, string displayName, string role)
    {
        UserId = userId;
        DisplayName = displayName;
        Role = role;
    }
}

/// <summary>
/// Ростер комнаты — список подключённых участников и их число. Рассылается
/// хабом в группу комнаты при входе/выходе участников. Контракт общий для
/// бэкенда и десктопа (как и GameCommand)
/// </summary>
public sealed class RoomRoster
{
    public int Online { get; set; }
    public List<RoomMember> Members { get; set; } = new();

    public RoomRoster() { }

    public RoomRoster(int online, List<RoomMember> members)
    {
        Online = online;
        Members = members;
    }
}
