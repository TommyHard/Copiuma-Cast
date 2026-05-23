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
