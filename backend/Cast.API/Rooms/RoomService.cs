using Cast.API.Data;
using Cast.API.Domain;
using Microsoft.EntityFrameworkCore;

namespace Cast.API.Rooms;

/// <summary>
/// Логика комнат: создание (стример становится владельцем), вход зрителя по коду,
/// разрешение членства и роли
/// </summary>
public sealed class RoomService
{
    private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private static readonly Random Rng = new();

    private readonly CastDbContext _db;

    public RoomService(CastDbContext db) => _db = db;

    public async Task<Room> CreateAsync(Guid ownerId, CreateRoomRequest req, CancellationToken ct = default)
    {
        var room = new Room
        {
            Title = string.IsNullOrWhiteSpace(req.Title) ? "Комната" : req.Title.Trim(),
            GameId = req.GameId,
            OwnerId = ownerId,
            Code = await GenerateUniqueCodeAsync(ct)
        };
        _db.Rooms.Add(room);
        _db.RoomMemberships.Add(new RoomMembership
        {
            RoomId = room.Id,
            UserId = ownerId,
            Role = RoomRole.Streamer
        });
        await _db.SaveChangesAsync(ct);
        return room;
    }

    /// <summary>
    /// Вход по коду. Если пользователь ещё не в комнате — добавляем зрителем
    /// </summary>
    public async Task<(Room room, RoomRole role)?> JoinByCodeAsync(Guid userId, string code, CancellationToken ct = default)
    {
        var room = await _db.Rooms.FirstOrDefaultAsync(r => r.Code == code, ct);
        if (room is null || !room.IsOpen)
            return null;

        var membership = await _db.RoomMemberships
            .FirstOrDefaultAsync(m => m.RoomId == room.Id && m.UserId == userId, ct);

        if (membership is null)
        {
            membership = new RoomMembership { RoomId = room.Id, UserId = userId, Role = RoomRole.Viewer };
            _db.RoomMemberships.Add(membership);
            await _db.SaveChangesAsync(ct);
        }

        return (room, membership.Role);
    }

    public async Task<(Room room, RoomRole role)?> ResolveMembershipAsync(Guid userId, string code, CancellationToken ct = default)
    {
        var room = await _db.Rooms.FirstOrDefaultAsync(r => r.Code == code, ct);
        if (room is null)
            return null;
        var membership = await _db.RoomMemberships
            .FirstOrDefaultAsync(m => m.RoomId == room.Id && m.UserId == userId, ct);
        return membership is null ? null : (room, membership.Role);
    }

    /// <summary>
    /// Комната по идентификатору (или null)
    /// </summary>
    public Task<Room?> GetAsync(Guid roomId, CancellationToken ct = default)
        => _db.Rooms.FirstOrDefaultAsync(r => r.Id == roomId, ct);

    /// <summary>
    /// Выключено ли событие администратором глобально для игры
    /// </summary>
    public Task<bool> IsEventGloballyDisabledAsync(string gameId, string eventId, CancellationToken ct = default)
        => _db.GameEventOverrides.AnyAsync(o => o.GameId == gameId && o.EventId == eventId && !o.Enabled, ct);

    /// <summary>
    /// Глобально выключенные события игры (для листинга)
    /// </summary>
    public async Task<HashSet<string>> GloballyDisabledEventsAsync(string? gameId, CancellationToken ct = default)
        => string.IsNullOrEmpty(gameId)
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : (await _db.GameEventOverrides.Where(o => o.GameId == gameId && !o.Enabled).Select(o => o.EventId).ToListAsync(ct))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Выключено ли событие стримером в этой комнате
    /// </summary>
    public Task<bool> IsEventDisabledAsync(Guid roomId, string eventId, CancellationToken ct = default)
        => _db.RoomEventToggles.AnyAsync(t => t.RoomId == roomId && t.EventId == eventId && !t.Enabled, ct);

    /// <summary>
    /// Множество выключенных событий комнаты (для отображения списка)
    /// </summary>
    public async Task<HashSet<string>> DisabledEventsAsync(Guid roomId, CancellationToken ct = default)
        => (await _db.RoomEventToggles
            .Where(t => t.RoomId == roomId && !t.Enabled)
            .Select(t => t.EventId)
            .ToListAsync(ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Включить/выключить событие в комнате. Только владелец-стример. Возвращает
    /// false, если комнаты нет или вызвавший не владелец
    /// </summary>
    public async Task<bool> SetEventEnabledAsync(Guid ownerId, Guid roomId, string eventId, bool enabled, CancellationToken ct = default)
    {
        var room = await _db.Rooms.FirstOrDefaultAsync(r => r.Id == roomId, ct);
        if (room is null || room.OwnerId != ownerId)
            return false;

        var toggle = await _db.RoomEventToggles
            .FirstOrDefaultAsync(t => t.RoomId == roomId && t.EventId == eventId, ct);
        if (toggle is null)
        {
            toggle = new RoomEventToggle { RoomId = roomId, EventId = eventId, Enabled = enabled };
            _db.RoomEventToggles.Add(toggle);
        }
        else
        {
            toggle.Enabled = enabled;
        }
        await _db.SaveChangesAsync(ct);
        return true;
    }

    private async Task<string> GenerateUniqueCodeAsync(CancellationToken ct)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var code = GenerateCode(6);
            if (!await _db.Rooms.AnyAsync(r => r.Code == code, ct))
                return code;
        }
        return GenerateCode(10);
    }

    private static string GenerateCode(int length)
    {
        Span<char> buffer = stackalloc char[length];
        lock (Rng)
        {
            for (var i = 0; i < length; i++)
                buffer[i] = CodeAlphabet[Rng.Next(CodeAlphabet.Length)];
        }
        return new string(buffer);
    }
}