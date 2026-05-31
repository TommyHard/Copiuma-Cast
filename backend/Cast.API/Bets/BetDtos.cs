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
    DateTimeOffset? ResolvedAt,
    Guid? WinningOutcomeId,
    long TotalPool,
    IReadOnlyList<BetOutcomeDto> Outcomes,
    // Итоги после разрешения: крупнейший выигрыш (имя + сумма) и общий выплаченный пул
    string? TopWinnerName,
    long TopWinnerPayout,
    long PaidOut,
    // Личный итог запросившего пользователя (null, если он не ставил)
    long? MyStake,
    long? MyPayout,
    string? MyStatus)
{
    /// <summary>
    /// Собрать DTO ставки с пулами, коэффициентами и (если задан forUserId)
    /// личным итогом пользователя
    /// </summary>
    public static async Task<BetDto?> LoadAsync(CastDbContext db, Guid betId, Guid? forUserId = null, CancellationToken ct = default)
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

        // Крупнейший выигрыш и общая выплата (после разрешения)
        string? topWinnerName = null;
        long topWinnerPayout = 0;
        long paidOut = bet.Wagers.Where(w => w.Status is WagerStatus.Won or WagerStatus.Refunded).Sum(w => w.Payout);
        var topWager = bet.Wagers
            .Where(w => w.Status == WagerStatus.Won)
            .OrderByDescending(w => w.Payout)
            .FirstOrDefault();
        if (topWager is not null)
        {
            topWinnerPayout = topWager.Payout;
            topWinnerName = await db.Users.AsNoTracking()
                .Where(u => u.Id == topWager.UserId)
                .Select(u => u.DisplayName)
                .FirstOrDefaultAsync(ct);
        }

        // Личный итог запросившего пользователя
        long? myStake = null, myPayout = null;
        string? myStatus = null;
        if (forUserId is not null)
        {
            var mine = bet.Wagers.Where(w => w.UserId == forUserId.Value).ToList();
            if (mine.Count > 0)
            {
                myStake = mine.Sum(w => w.Amount);
                myPayout = mine.Sum(w => w.Payout);
                // Статус: выиграл, если есть выигравшая ставка; иначе возврат/проигрыш/в игре
                myStatus = mine.Any(w => w.Status == WagerStatus.Won) ? nameof(WagerStatus.Won)
                    : mine.Any(w => w.Status == WagerStatus.Refunded) ? nameof(WagerStatus.Refunded)
                    : mine.Any(w => w.Status == WagerStatus.Lost) ? nameof(WagerStatus.Lost)
                    : nameof(WagerStatus.Placed);
            }
        }

        return new BetDto(bet.Id, bet.RoomId, bet.StreamerId, bet.Title, bet.Status,
            bet.LocksAt, bet.ResolvedAt, bet.WinningOutcomeId, totalPool, outcomes,
            topWinnerName, topWinnerPayout, paidOut, myStake, myPayout, myStatus);
    }

    public static async Task<List<BetDto>> LoadRoomAsync(CastDbContext db, Guid roomId, Guid? forUserId = null, CancellationToken ct = default)
    {
        var ids = await db.Bets.AsNoTracking()
            .Where(b => b.RoomId == roomId)
            .OrderByDescending(b => b.CreatedAt)
            .Select(b => b.Id)
            .ToListAsync(ct);

        var result = new List<BetDto>(ids.Count);
        foreach (var id in ids)
        {
            var dto = await LoadAsync(db, id, forUserId, ct);
            if (dto is not null) result.Add(dto);
        }
        return result;
    }
}
