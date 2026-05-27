using Cast.API.Common;
using Cast.API.Data;
using Cast.API.Domain;
using Cast.API.Economy;
using Cast.API.Realtime;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Cast.API.Bets;

public enum WagerError { None, BetNotFound, BetClosed, UnknownOutcome, InsufficientFunds, InvalidAmount }

/// <summary>
/// Движок ставок (паримьютюэль). Приём ставки списывает валюту, разрешение
/// распределяет весь пул победителям пропорционально вкладу, отмена возвращает
/// ставки. Все денежные операции идут через <see cref="WalletService"/> с
/// идемпотентными ключами, поэтому повторное разрешение/возврат не задваивают
/// выплаты. Изменения транслируются в комнату по SignalR
/// </summary>
public sealed class BettingService
{
    private readonly CastDbContext _db;
    private readonly WalletService _wallet;
    private readonly IHubContext<RoomHub> _hub;

    public BettingService(CastDbContext db, WalletService wallet, IHubContext<RoomHub> hub)
    {
        _db = db;
        _wallet = wallet;
        _hub = hub;
    }

    public async Task<Bet> CreateBetAsync(Guid streamerId, Guid roomId, string title,
        IEnumerable<string> outcomeLabels, int locksInSeconds, CancellationToken ct = default)
    {
        var bet = new Bet
        {
            RoomId = roomId,
            StreamerId = streamerId,
            Title = title.Trim(),
            LocksAt = DateTimeOffset.UtcNow.AddSeconds(Math.Clamp(locksInSeconds, 5, 3600)),
            Outcomes = outcomeLabels
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .Select(l => new BetOutcome { Label = l.Trim() })
                .ToList()
        };
        _db.Bets.Add(bet);
        await _db.SaveChangesAsync(ct);
        await BroadcastAsync(bet.Id, ct);
        return bet;
    }

    public async Task<(WagerError error, long balance)> PlaceWagerAsync(Guid userId, Guid betId,
        Guid outcomeId, long amount, CancellationToken ct = default)
    {
        if (amount <= 0)
            return (WagerError.InvalidAmount, 0);

        var bet = await _db.Bets.Include(b => b.Outcomes)
            .FirstOrDefaultAsync(b => b.Id == betId, ct);
        if (bet is null)
            return (WagerError.BetNotFound, 0);
        if (bet.Status != BetStatus.Open || DateTimeOffset.UtcNow >= bet.LocksAt)
            return (WagerError.BetClosed, 0);
        if (bet.Outcomes.All(o => o.Id != outcomeId))
            return (WagerError.UnknownOutcome, 0);

        var wager = new BetWager
        {
            BetId = betId,
            OutcomeId = outcomeId,
            UserId = userId,
            StreamerId = bet.StreamerId,
            Amount = amount
        };

        // Списание у стримера ставки; Id ставки служит ключом идемпотентности
        var charge = await _wallet.ChargeAsync(wager.Id, userId, bet.StreamerId, bet.RoomId,
            $"bet:{betId}", amount, ct);
        if (charge.Status == ChargeStatus.InsufficientFunds)
            return (WagerError.InsufficientFunds, charge.Balance);

        _db.BetWagers.Add(wager);
        await _db.SaveChangesAsync(ct);
        await BroadcastAsync(betId, ct);
        return (WagerError.None, charge.Balance);
    }

    /// <summary>
    /// Разрешить ставку: распределить пул победителям пропорционально вкладу.
    /// Если победителей нет — возврат всем. Только владелец-стример
    /// </summary>
    public async Task<bool> ResolveBetAsync(Guid streamerId, Guid betId, Guid winningOutcomeId, CancellationToken ct = default)
    {
        var bet = await _db.Bets.Include(b => b.Wagers).Include(b => b.Outcomes)
            .FirstOrDefaultAsync(b => b.Id == betId, ct);
        if (bet is null || bet.StreamerId != streamerId || bet.Status != BetStatus.Open)
            return false;
        if (bet.Outcomes.All(o => o.Id != winningOutcomeId))
            return false;

        long totalPool = bet.Wagers.Sum(w => w.Amount);
        long winnersPool = bet.Wagers.Where(w => w.OutcomeId == winningOutcomeId).Sum(w => w.Amount);

        foreach (var w in bet.Wagers)
        {
            if (winnersPool == 0)
            {
                // Никто не угадал — возврат всем
                w.Status = WagerStatus.Refunded;
                w.Payout = w.Amount;
                await _wallet.CreditAsync(DeterministicGuid.Create($"betrefund:{w.Id}"),
                    w.UserId, w.StreamerId, bet.RoomId, $"bet_refund:{betId}", w.Amount, ct);
            }
            else if (w.OutcomeId == winningOutcomeId)
            {
                w.Status = WagerStatus.Won;
                w.Payout = (long)((decimal)w.Amount * totalPool / winnersPool);
                await _wallet.CreditAsync(DeterministicGuid.Create($"betpayout:{w.Id}"),
                    w.UserId, w.StreamerId, bet.RoomId, $"bet_win:{betId}", w.Payout, ct);
            }
            else
            {
                w.Status = WagerStatus.Lost;
            }
        }

        bet.Status = BetStatus.Resolved;
        bet.WinningOutcomeId = winningOutcomeId;
        bet.ResolvedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        await BroadcastAsync(betId, ct);
        return true;
    }

    /// <summary>
    /// Отменить одну открытую ставку с возвратом (владелец-стример)
    /// </summary>
    public async Task<bool> CancelBetAsync(Guid streamerId, Guid betId, string reason, CancellationToken ct = default)
    {
        var bet = await _db.Bets.Include(b => b.Wagers).FirstOrDefaultAsync(b => b.Id == betId, ct);
        if (bet is null || bet.StreamerId != streamerId || bet.Status != BetStatus.Open)
            return false;
        await RefundBetAsync(bet, reason, ct);
        return true;
    }

    /// <summary>
    /// Отменить все открытые ставки комнаты с полным возвратом. Используется и
    /// вручную, и Dead Man's Switch при уходе стримера
    /// </summary>
    public async Task CancelOpenBetsForRoomAsync(Guid roomId, string reason, CancellationToken ct = default)
    {
        var bets = await _db.Bets.Include(b => b.Wagers)
            .Where(b => b.RoomId == roomId && b.Status == BetStatus.Open)
            .ToListAsync(ct);

        foreach (var bet in bets)
            await RefundBetAsync(bet, reason, ct);
    }

    private async Task RefundBetAsync(Bet bet, string reason, CancellationToken ct)
    {
        foreach (var w in bet.Wagers.Where(w => w.Status == WagerStatus.Placed))
        {
            w.Status = WagerStatus.Refunded;
            w.Payout = w.Amount;
            await _wallet.CreditAsync(DeterministicGuid.Create($"betrefund:{w.Id}"),
                w.UserId, w.StreamerId, bet.RoomId, $"bet_cancel:{reason}", w.Amount, ct);
        }
        bet.Status = BetStatus.Cancelled;
        bet.ResolvedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        await BroadcastAsync(bet.Id, ct);
    }

    private async Task BroadcastAsync(Guid betId, CancellationToken ct)
    {
        var dto = await BetDto.LoadAsync(_db, betId, ct);
        if (dto is not null)
            await _hub.Clients.Group(RoomHub.RoomGroup(dto.RoomId)).SendAsync("BetUpdated", dto, ct);
    }
}