using Cast.API.Data;
using Cast.API.Domain;
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