using Cast.API.Data;
using Cast.API.Domain;
using Microsoft.EntityFrameworkCore;

namespace Cast.API.Bets;

public sealed record CreateBetRequest(string Title, List<string> Outcomes, int LocksInSeconds);
public sealed record PlaceWagerRequest(Guid OutcomeId, long Amount);
public sealed record ResolveBetRequest(Guid WinningOutcomeId);

/// <summary>
/// Исход ставки с агрегатами: сколько поставлено и текущий паримьютюэль-
/// коэффициент (весь пул / пул исхода). Odds = null, если на исход ещё не
/// ставили
/// </summary>
public sealed record BetOutcomeDto(Guid Id, string Label, long Pool, decimal? Odds);

public sealed record BetDto(
    Guid Id,
    Guid RoomId,
    Guid StreamerId,
    string Title,
    BetStatus Status,
    DateTimeOffset LocksAt,
    Guid? WinningOutcomeId,
    long TotalPool,
    IReadOnlyList<BetOutcomeDto> Outcomes)
{
    /// <summary>
    /// Собрать DTO ставки с пулами и коэффициентами по данным БД
    /// </summary>
    public static async Task<BetDto?> LoadAsync(CastDbContext db, Guid betId, CancellationToken ct = default)
    {
        var bet = await db.Bets.AsNoTracking()
            .Include(b => b.Outcomes)
            .Include(b => b.Wagers)
            .FirstOrDefaultAsync(b => b.Id == betId, ct);
        if (bet is null)
            return null;

        long totalPool = bet.Wagers.Sum(w => w.Amount);
        var outcomes = bet.Outcomes.Select(o =>
        {
            long pool = bet.Wagers.Where(w => w.OutcomeId == o.Id).Sum(w => w.Amount);
            decimal? odds = pool > 0 ? Math.Round((decimal)totalPool / pool, 2) : null;
            return new BetOutcomeDto(o.Id, o.Label, pool, odds);
        }).ToList();

        return new BetDto(bet.Id, bet.RoomId, bet.StreamerId, bet.Title, bet.Status,
            bet.LocksAt, bet.WinningOutcomeId, totalPool, outcomes);
    }

    public static async Task<List<BetDto>> LoadRoomAsync(CastDbContext db, Guid roomId, CancellationToken ct = default)
    {
        var ids = await db.Bets.AsNoTracking()
            .Where(b => b.RoomId == roomId)
            .OrderByDescending(b => b.CreatedAt)
            .Select(b => b.Id)
            .ToListAsync(ct);

        var result = new List<BetDto>(ids.Count);
        foreach (var id in ids)
        {
            var dto = await LoadAsync(db, id, ct);
            if (dto is not null) result.Add(dto);
        }
        return result;
    }
}