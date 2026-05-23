using Cast.API.Common;
using Cast.API.Events;
using Cast.API.Rooms;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Cast.API.Realtime;

/// <summary>
/// Реал-тайм-канал комнаты. Зритель входит в комнату и инициирует события;
/// стример (владелец) попадает в отдельную группу, куда консьюмер доставляет
/// готовые игровые команды. Группы:
///   room:{roomId}     — все участники (чат/уведомления),
///   streamer:{roomId} — соединение(я) стримера (доставка команд моду)
/// </summary>
[Authorize]
public sealed class RoomHub : Hub
{
    private readonly RoomService _rooms;
    private readonly IEventBus _bus;

    public RoomHub(RoomService rooms, IEventBus bus)
    {
        _rooms = rooms;
        _bus = bus;
    }

    public static string RoomGroup(Guid roomId) => $"room:{roomId}";
    public static string StreamerGroup(Guid roomId) => $"streamer:{roomId}";

    /// <summary>
    /// Подключиться к комнате по коду. Возвращает информацию о комнате/роли
    /// </summary>
    public async Task<RoomDto> JoinRoom(string code)
    {
        var userId = Context.User?.GetUserId()
            ?? throw new HubException("Не авторизован.");

        var resolved = await _rooms.JoinByCodeAsync(userId, code.ToUpperInvariant());
        if (resolved is null)
            throw new HubException("Комната не найдена или закрыта.");

        var (room, role) = resolved.Value;
        await Groups.AddToGroupAsync(Context.ConnectionId, RoomGroup(room.Id));
        if (role == Domain.RoomRole.Streamer)
            await Groups.AddToGroupAsync(Context.ConnectionId, StreamerGroup(room.Id));

        return RoomDto.From(room, role);
    }

    /// <summary>
    /// Зритель инициирует событие. Хаб только публикует сообщение в очередь —
    /// валидацию по манифесту, списание валюты и доставку делает консьюмер
    /// </summary>
    public async Task TriggerEvent(string code, string eventId, Dictionary<string, string>? args)
    {
        var userId = Context.User?.GetUserId()
            ?? throw new HubException("Не авторизован.");
        var username = Context.User?.FindFirst("displayName")?.Value ?? "viewer";

        var resolved = await _rooms.ResolveMembershipAsync(userId, code.ToUpperInvariant());
        if (resolved is null)
            throw new HubException("Вы не состоите в этой комнате.");

        var (room, _) = resolved.Value;

        await _bus.PublishAsync(new EventMessage
        {
            RoomId = room.Id,
            RoomCode = room.Code,
            UserId = userId,
            Username = username,
            EventId = eventId,
            Args = args ?? new Dictionary<string, string>()
        });
    }
}
