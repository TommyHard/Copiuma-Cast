using Cast.API.Common;
using Cast.API.Social;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Cast.API.Realtime;

/// <summary>
/// Канал платформенного присутствия. Клиент держит это соединение, пока открыт
/// (вне зависимости от комнат). По подключению/отключению обновляется онлайн-
/// статус и друзьям рассылается PresenceChanged
/// </summary>
[Authorize]
public sealed class PresenceHub : Hub
{
    private readonly OnlinePresenceService _presence;

    public PresenceHub(OnlinePresenceService presence) => _presence = presence;

    /// <summary>
    /// Группа для адресной доставки статуса конкретному пользователю
    /// </summary>
    public static string UserGroup(Guid userId) => $"user:{userId}";

    public override async Task OnConnectedAsync()
    {
        var userId = Context.User?.GetUserId();
        if (userId is not null)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(userId.Value));
            await _presence.ConnectAsync(userId.Value, Context.ConnectionId);
        }
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await _presence.DisconnectAsync(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }
}