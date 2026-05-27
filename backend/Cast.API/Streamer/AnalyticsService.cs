using Cast.API.Data;
using Microsoft.EntityFrameworkCore;

namespace Cast.API.Streamer;

/// <summary>
/// Аналитика стримера: оборот валюты, топ-10 зрителей по тратам, популярные
/// события и медиа. Считается из журнала проводок (CoinTransaction, привязан к
/// стримеру) и журнала событий (EventLog по комнатам стримера)
/// </summary>
public sealed class AnalyticsService
{
    private const int TopN = 10;

    private readonly CastDbContext _db;

    public AnalyticsService(CastDbContext db) => _db = db;

    public async Task<AnalyticsDto> GetDashboardAsync(Guid streamerId, CancellationToken ct = default)
    {
        var roomIds = _db.Rooms.Where(r => r.OwnerId == streamerId).Select(r => r.Id);

        // Оборот: траты (Amount < 0) и начисления (Amount > 0) у этого стримера
        var spentSum = await _db.CoinTransactions
            .Where(t => t.StreamerId == streamerId && t.Amount < 0)
            .SumAsync(t => (long?)t.Amount, ct) ?? 0;
        var creditedSum = await _db.CoinTransactions
            .Where(t => t.StreamerId == streamerId && t.Amount > 0)
            .SumAsync(t => (long?)t.Amount, ct) ?? 0;

        // Топ-зрители по тратам (наибольшие по модулю отрицательные суммы)
        var spenders = await _db.CoinTransactions
            .Where(t => t.StreamerId == streamerId && t.Amount < 0)
            .GroupBy(t => t.UserId)
            .Select(g => new { UserId = g.Key, Sum = g.Sum(x => x.Amount) })
            .OrderBy(x => x.Sum)
            .Take(TopN)
            .ToListAsync(ct);

        var spenderIds = spenders.Select(s => s.UserId).ToList();
        var users = await _db.Users
            .Where(u => spenderIds.Contains(u.Id))
            .Select(u => new { u.Id, u.DisplayName, u.Handle, u.AvatarUrl })
            .ToDictionaryAsync(u => u.Id, ct);

        var topViewers = spenders.Select(s =>
        {
            users.TryGetValue(s.UserId, out var u);
            return new ViewerStatDto(s.UserId, u?.DisplayName ?? "—", u?.Handle ?? "", u?.AvatarUrl, -s.Sum);
        }).ToList();

        // Популярные события
        var popularEvents = await _db.EventLog
            .Where(e => roomIds.Contains(e.RoomId))
            .GroupBy(e => e.EventId)
            .Select(g => new EventStatDto(g.Key, g.LongCount()))
            .OrderByDescending(x => x.Count)
            .Take(TopN)
            .ToListAsync(ct);

        // Популярные медиа
        var mediaCounts = await _db.EventLog
            .Where(e => roomIds.Contains(e.RoomId) && e.MediaId != null)
            .GroupBy(e => e.MediaId)
            .Select(g => new { MediaId = g.Key, Count = g.LongCount() })
            .OrderByDescending(x => x.Count)
            .Take(TopN)
            .ToListAsync(ct);

        var mediaIds = mediaCounts.Where(m => m.MediaId != null).Select(m => m.MediaId!.Value).ToList();
        var titles = await _db.MediaItems
            .Where(m => mediaIds.Contains(m.Id))
            .ToDictionaryAsync(m => m.Id, m => m.Title, ct);

        var popularMedia = mediaCounts
            .Where(m => m.MediaId != null)
            .Select(m => new MediaStatDto(m.MediaId!.Value,
                titles.GetValueOrDefault(m.MediaId!.Value, "(удалено)"), m.Count))
            .ToList();

        return new AnalyticsDto(-spentSum, creditedSum, topViewers, popularEvents, popularMedia);
    }
}