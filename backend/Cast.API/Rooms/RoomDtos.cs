using Cast.API.Domain;

namespace Cast.API.Rooms;

public sealed record CreateRoomRequest(string Title, string? GameId);

public sealed record RoomDto(
    Guid Id,
    string Code,
    string Title,
    string? GameId,
    bool IsOpen,
    RoomRole Role)
{
    public static RoomDto From(Room room, RoomRole role)
        => new(room.Id, room.Code, room.Title, room.GameId, room.IsOpen, role);
}
