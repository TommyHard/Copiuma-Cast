using Cast.API.Data;
using Cast.API.Domain;
using Microsoft.EntityFrameworkCore;

namespace Cast.API.Social;

/// <summary>
/// Вычисляет отображаемый статус пользователей. Онлайн определяется наличием
/// активного платформенного соединения (UserConnection). Ручной статус
/// (Away/DnD/«невидимка») уточняет отображение, а активность в комнате
/// (Watching/Playing) добавляется поверх. Нет соединения или режим "Offline" ->
/// показываем Offline
/// </summary>
public sealed class StatusService
{
    private readonly CastDbContext _db;

    public StatusService(CastDbContext db) => _db = db;

    public async Task<Dictionary<Guid, (UserStatus status, ActivityKind activity, string? target)>>
        ResolveAsync(IReadOnlyCollection<Guid> userIds, CancellationToken ct = default)
    {
        var result = new Dictionary<Guid, (UserStatus, ActivityKind, string?)>();
        if (userIds.Count == 0)
            return result;

        // Ручной статус
        var manual = await _db.Users
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.ManualStatus })
            .ToDictionaryAsync(u => u.Id, u => u.ManualStatus, ct);

        // Кто сейчас онлайн на платформе (есть активное соединение)
        var connected = (await _db.UserConnections
            .Where(c => userIds.Contains(c.UserId))
            .Select(c => c.UserId)
            .Distinct()
            .ToListAsync(ct)).ToHashSet();

        // Активность из присутствия: роль + игра комнаты + имя владельца
        var presence = await (
            from c in _db.RoomConnections
            where userIds.Contains(c.UserId)
            join r in _db.Rooms on c.RoomId equals r.Id
            join owner in _db.Users on r.OwnerId equals owner.Id
            select new { c.UserId, c.Role, r.GameId, OwnerName = owner.DisplayName })
            .ToListAsync(ct);

        // Названия игр для статуса Playing
        var slugs = presence.Where(p => p.GameId != null).Select(p => p.GameId!).Distinct().ToList();
        var gameTitles = await _db.Games
            .Where(g => slugs.Contains(g.Slug))
            .ToDictionaryAsync(g => g.Slug, g => g.Title, ct);

        foreach (var id in userIds)
        {
            var manualStatus = manual.GetValueOrDefault(id, UserStatus.Offline);

            // Не онлайн или режим "невидимка" (Offline вручную) -> Offline, без активности
            if (!connected.Contains(id) || manualStatus == UserStatus.Offline)
            {
                result[id] = (UserStatus.Offline, ActivityKind.None, null);
                continue;
            }

            // Онлайн: Away/DnD сохраняем, иначе Online
            var status = manualStatus is UserStatus.Away or UserStatus.DoNotDisturb
                ? manualStatus
                : UserStatus.Online;

            var mine = presence.Where(p => p.UserId == id).ToList();
            var asStreamer = mine.FirstOrDefault(p => p.Role == RoomRole.Streamer);
            if (asStreamer is not null)
            {
                var title = asStreamer.GameId is { } g && gameTitles.TryGetValue(g, out var t) ? t : asStreamer.GameId;
                result[id] = (status, ActivityKind.Playing, title);
                continue;
            }

            var asViewer = mine.FirstOrDefault(p => p.Role == RoomRole.Viewer);
            if (asViewer is not null)
            {
                result[id] = (status, ActivityKind.Watching, asViewer.OwnerName);
                continue;
            }

            result[id] = (status, ActivityKind.None, null);
        }

        return result;
    }
}