using Cast.API.Data;
using Cast.API.Domain;
using Cast.API.Realtime;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Cast.API.Social;

/// <summary>
/// Учёт платформенных соединений (PresenceHub) и рассылка изменений статуса
/// друзьям в реальном времени. Онлайн-статус определяется наличием активного
/// соединения, поэтому StatusService может отличать "онлайн вне комнаты" от
/// офлайна
/// </summary>
public sealed class OnlinePresenceService
{
    private readonly CastDbContext _db;
    private readonly SocialService _social;
    private readonly IHubContext<PresenceHub> _hub;

    public OnlinePresenceService(CastDbContext db, SocialService social, IHubContext<PresenceHub> hub)
    {
        _db = db;
        _social = social;
        _hub = hub;
    }

    public async Task ConnectAsync(Guid userId, string connectionId, CancellationToken ct = default)
    {
        await _db.UserConnections.Where(c => c.ConnectionId == connectionId).ExecuteDeleteAsync(ct);
        _db.UserConnections.Add(new UserConnection { UserId = userId, ConnectionId = connectionId });
        await _db.SaveChangesAsync(ct);
        await BroadcastToFriendsAsync(userId, ct);
    }

    public async Task DisconnectAsync(string connectionId, CancellationToken ct = default)
    {
        var conn = await _db.UserConnections.FirstOrDefaultAsync(c => c.ConnectionId == connectionId, ct);
        if (conn is null)
            return;

        var userId = conn.UserId;
        _db.UserConnections.Remove(conn);
        await _db.SaveChangesAsync(ct);

        // Шлём обновление, только если ушло последнее соединение (стал офлайн)
        if (!await _db.UserConnections.AnyAsync(c => c.UserId == userId, ct))
            await BroadcastToFriendsAsync(userId, ct);
    }

    /// <summary>
    /// Уведомить друзей об изменении активности пользователя (вошёл/вышел из
    /// комнаты -> статус "Играет в ..." / "Смотрит ..."). Шлёт актуальную карточку
    /// </summary>
    public Task NotifyActivityChangedAsync(Guid userId, CancellationToken ct = default)
        => BroadcastToFriendsAsync(userId, ct);

    private async Task BroadcastToFriendsAsync(Guid userId, CancellationToken ct)
    {
        var card = await _social.CardAsync(userId, ct);
        if (card is null)
            return;

        var friendIds = await _db.FriendLinks
            .Where(f => f.Status == FriendStatus.Accepted && (f.RequesterId == userId || f.AddresseeId == userId))
            .Select(f => f.RequesterId == userId ? f.AddresseeId : f.RequesterId)
            .ToListAsync(ct);
        if (friendIds.Count == 0)
            return;

        var groups = friendIds.Select(PresenceHub.UserGroup).ToList();
        await _hub.Clients.Groups(groups).SendAsync("PresenceChanged", card, ct);
    }
}