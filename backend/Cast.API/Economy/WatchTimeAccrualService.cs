using Cast.API.Common;
using Cast.API.Data;
using Cast.API.Realtime;
using Microsoft.EntityFrameworkCore;

namespace Cast.API.Economy;

/// <summary>
/// Фоновое начисление watch-time: раз в интервал начисляет монеты активным
/// зрителям на их баланс у стримера комнаты. Идемпотентность обеспечивается
/// детерминированным ключом проводки по «корзине времени», поэтому повторный
/// прогон того же интервала (рестарт, дубль) не начислит дважды
/// </summary>
public sealed class WatchTimeAccrualService : BackgroundService
{
    private const int IntervalSeconds = 60;
    private const long CoinsPerTick = 10;

    private readonly IServiceProvider _services;
    private readonly ILogger<WatchTimeAccrualService> _logger;

    public WatchTimeAccrualService(IServiceProvider services, ILogger<WatchTimeAccrualService> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(IntervalSeconds));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await AccrueOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Сбой начисления watch-time.");
            }
        }
    }

    private async Task AccrueOnceAsync(CancellationToken ct)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CastDbContext>();
        var presence = scope.ServiceProvider.GetRequiredService<PresenceService>();
        var wallet = scope.ServiceProvider.GetRequiredService<WalletService>();

        var viewers = await presence.ActiveViewersAsync(ct);
        if (viewers.Count == 0)
            return;

        // Владельцы (стримеры) комнат одним запросом
        var roomIds = viewers.Select(v => v.RoomId).Distinct().ToList();
        var owners = await db.Rooms
            .Where(r => roomIds.Contains(r.Id))
            .Select(r => new { r.Id, r.OwnerId })
            .ToDictionaryAsync(r => r.Id, r => r.OwnerId, ct);

        // Фиксирует интервал — основа идемпотентного ключа
        var bucket = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / IntervalSeconds;

        foreach (var (roomId, userId) in viewers)
        {
            if (!owners.TryGetValue(roomId, out var streamerId) || streamerId == userId)
                continue;

            var opId = DeterministicGuid.Create($"watchtime:{userId}:{streamerId}:{bucket}");
            await wallet.CreditAsync(opId, userId, streamerId, roomId, "watchtime", CoinsPerTick, ct);
        }
    }
}