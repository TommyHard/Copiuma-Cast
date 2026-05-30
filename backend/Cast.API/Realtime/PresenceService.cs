using Cast.API.Data;
using Cast.API.Domain;
using Cast.Shared.GameBridge;
using Microsoft.EntityFrameworkCore;

namespace Cast.API.Realtime;

/// <summary>
/// Учёт присутствия в комнатах по SignalR-соединениям. Источник данных для
/// начисления watch-time и для Dead Man's Switch (уход стримера)
/// </summary>
public sealed class PresenceService
{
    private readonly CastDbContext _db;

    public PresenceService(CastDbContext db) => _db = db;

    /// <summary>
    /// Зафиксировать вход соединения в комнату (идемпотентно по ConnectionId)
    /// </summary>
    public async Task JoinAsync(Guid roomId, Guid userId, RoomRole role, string connectionId, CancellationToken ct = default)
    {
        await _db.RoomConnections
            .Where(c => c.ConnectionId == connectionId)
            .ExecuteDeleteAsync(ct);
        _db.RoomConnections.Add(new RoomConnection
        {
            RoomId = roomId,
            UserId = userId,
            Role = role,
            ConnectionId = connectionId
        });
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Снять присутствие по соединению. Возвращает покинутую комнату/роль,
    /// если строка была (нужно для Dead Man's Switch), иначе null
    /// </summary>
    public async Task<(Guid roomId, RoomRole role)?> LeaveAsync(string connectionId, CancellationToken ct = default)
    {
        var conn = await _db.RoomConnections.FirstOrDefaultAsync(c => c.ConnectionId == connectionId, ct);
        if (conn is null)
            return null;
        _db.RoomConnections.Remove(conn);
        await _db.SaveChangesAsync(ct);
        return (conn.RoomId, conn.Role);
    }

    /// <summary>
    /// Остались ли в комнате активные соединения стримера
    /// </summary>
    public Task<bool> HasStreamerAsync(Guid roomId, CancellationToken ct = default)
        => _db.RoomConnections.AnyAsync(c => c.RoomId == roomId && c.Role == RoomRole.Streamer, ct);

    /// <summary>
    /// Идентификаторы соединений пользователя в комнате
    /// </summary>
    public Task<List<string>> ConnectionIdsAsync(Guid roomId, Guid userId, CancellationToken ct = default)
        => _db.RoomConnections
            .Where(c => c.RoomId == roomId && c.UserId == userId)
            .Select(c => c.ConnectionId)
            .ToListAsync(ct);

    /// <summary>
    /// Снять все присутствия пользователя в комнате (при кике)
    /// </summary>
    public Task RemoveUserAsync(Guid roomId, Guid userId, CancellationToken ct = default)
        => _db.RoomConnections
            .Where(c => c.RoomId == roomId && c.UserId == userId)
            .ExecuteDeleteAsync(ct);

    /// <summary>
    /// Ростер комнаты: подключённые участники (без дублей по пользователю) с
    /// отображаемым именем и ролью, плюс их количество
    /// </summary>
    public async Task<RoomRoster> GetRosterAsync(Guid roomId, CancellationToken ct = default)
    {
        var rows = await (from c in _db.RoomConnections
                          where c.RoomId == roomId
                          join u in _db.Users on c.UserId equals u.Id
                          select new { c.UserId, u.DisplayName, c.Role })
            .ToListAsync(ct);

        var members = rows
            .GroupBy(r => r.UserId)
            .Select(g => new RoomMember(
                g.Key.ToString(),
                g.First().DisplayName,
                g.First().Role.ToString()))
            .ToList();

        return new RoomRoster(members.Count, members);
    }

    /// <summary>
    /// Активные зрители по комнатам: (RoomId, UserId) без дублей. Стримеры
    /// исключаются — себе watch-time не начисляется
    /// </summary>
    public async Task<List<(Guid RoomId, Guid UserId)>> ActiveViewersAsync(CancellationToken ct = default)
    {
        var rows = await _db.RoomConnections
            .Where(c => c.Role == RoomRole.Viewer)
            .Select(c => new { c.RoomId, c.UserId })
            .Distinct()
            .ToListAsync(ct);
        return rows.Select(r => (r.RoomId, r.UserId)).ToList();
    }
}