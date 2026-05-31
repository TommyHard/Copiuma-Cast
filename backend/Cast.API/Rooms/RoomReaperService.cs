using Cast.API.Data;
using Microsoft.EntityFrameworkCore;

namespace Cast.API.Rooms;

/// <summary>
/// Фоновое закрытие «висящих» комнат. Комната считается неактивной, если она
/// открыта, но к ней нет ни одного живого SignalR-соединения (RoomConnections).
/// Если комната остаётся пустой дольше порога — закрываем её (IsOpen = false).
///
/// Пустоту отслеживаем в памяти: на первом «пустом» тике запоминаем момент, на
/// последующих — закрываем по истечении порога. Любое появление соединения
/// сбрасывает отметку. Так короткий разрыв (перезагрузка страницы,
/// переподключение) не закрывает комнату преждевременно.
/// </summary>
public sealed class RoomReaperService : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan EmptyGrace = TimeSpan.FromMinutes(2);

    private readonly IServiceProvider _services;
    private readonly ILogger<RoomReaperService> _logger;

    // roomId -> момент, когда комната впервые увидена пустой
    private readonly Dictionary<Guid, DateTimeOffset> _emptySince = new();

    public RoomReaperService(IServiceProvider services, ILogger<RoomReaperService> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(CheckInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await ReapOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Сбой закрытия неактивных комнат.");
            }
        }
    }

    private async Task ReapOnceAsync(CancellationToken ct)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CastDbContext>();

        // Чистим просроченные непринятые приглашения (по сроку жизни)
        var inviteCutoff = DateTimeOffset.UtcNow - RoomService.InviteTtl;
        await db.RoomMemberships
            .Where(m => m.Pending && m.InvitedAt != null && m.InvitedAt < inviteCutoff)
            .ExecuteDeleteAsync(ct);

        // Удаляем закрытые ставки (разрешённые/отменённые) спустя 30 c — чтобы не
        // копились. Клиенты тоже прячут их по ResolvedAt, это чистка хранилища
        var betCutoff = DateTimeOffset.UtcNow - TimeSpan.FromSeconds(30);
        await db.Bets
            .Where(b => b.Status != Domain.BetStatus.Open && b.ResolvedAt != null && b.ResolvedAt < betCutoff)
            .ExecuteDeleteAsync(ct);

        // Все открытые комнаты и множество тех, к кому есть живые соединения
        var openRoomIds = await db.Rooms.Where(r => r.IsOpen).Select(r => r.Id).ToListAsync(ct);
        var connectedRoomIds = (await db.RoomConnections.Select(c => c.RoomId).Distinct().ToListAsync(ct))
            .ToHashSet();

        var now = DateTimeOffset.UtcNow;
        var toClose = new List<Guid>();

        foreach (var roomId in openRoomIds)
        {
            if (connectedRoomIds.Contains(roomId))
            {
                _emptySince.Remove(roomId); // есть подключения — не трогаем
                continue;
            }

            if (!_emptySince.TryGetValue(roomId, out var since))
            {
                _emptySince[roomId] = now; // впервые увидели пустой
                continue;
            }

            if (now - since >= EmptyGrace)
                toClose.Add(roomId);
        }

        // Чистим отметки для уже неактуальных комнат (закрытых вручную и т.п.)
        var openSet = openRoomIds.ToHashSet();
        foreach (var stale in _emptySince.Keys.Where(k => !openSet.Contains(k)).ToList())
            _emptySince.Remove(stale);

        if (toClose.Count == 0)
            return;

        await db.Rooms.Where(r => toClose.Contains(r.Id) && r.IsOpen)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.IsOpen, false), ct);

        foreach (var roomId in toClose)
            _emptySince.Remove(roomId);

        _logger.LogInformation("Закрыто неактивных комнат: {Count}.", toClose.Count);
    }
}
