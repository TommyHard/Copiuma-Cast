using Cast.API.Data;
using Cast.API.Domain;
using Microsoft.EntityFrameworkCore;

namespace Cast.API.Economy;

public enum ChargeStatus
{
    /// <summary>
    /// Средства успешно списаны
    /// </summary>
    Charged,
    /// <summary>
    /// Недостаточно средств — списание не выполнено
    /// </summary>
    InsufficientFunds,
    /// <summary>
    /// Операция с этим ключом уже обработана ранее (идемпотентность)
    /// </summary>
    AlreadyProcessed
}

public readonly record struct ChargeResult(ChargeStatus Status, long Balance)
{
    /// <summary>
    /// Можно продолжать (списано либо уже было обработано)
    /// </summary>
    public bool Ok => Status is ChargeStatus.Charged or ChargeStatus.AlreadyProcessed;
}

/// <summary>
/// Авторитетные операции с валютой, локализованной по стримеру
/// (<see cref="StreamerWallet"/>). Списание и начисление атомарны: один условный
/// UPDATE с проверкой остатка прямо в БД исключает гонки и перерасход. Каждая
/// операция фиксируется в журнале проводок с идемпотентным ключом
/// </summary>
public sealed class WalletService
{
    /// <summary>
    /// Стартовый баланс нового кошелька. Временная мера: со временем начальное
    /// наполнение целиком уйдёт в начисления (фарминг), а не константу
    /// </summary>
    public const long StartingBalance = 1000;

    private readonly CastDbContext _db;

    public WalletService(CastDbContext db) => _db = db;

    /// <summary>
    /// Текущий баланс зрителя у стримера (0, если кошелька ещё нет)
    /// </summary>
    public Task<long> GetBalanceAsync(Guid userId, Guid streamerId, CancellationToken ct = default)
        => _db.StreamerWallets
            .Where(w => w.UserId == userId && w.StreamerId == streamerId)
            .Select(w => w.Coins)
            .FirstOrDefaultAsync(ct);

    /// <summary>
    /// Списать стоимость с баланса зрителя у стримера. Идемпотентно по
    /// <paramref name="operationId"/>. Кошелёк создаётся при первом обращении.
    /// Бесплатные операции (amount <= 0) проходят без проводки
    /// </summary>
    public async Task<ChargeResult> ChargeAsync(Guid operationId, Guid userId, Guid streamerId,
        Guid roomId, string reason, long cost, CancellationToken ct = default)
    {
        if (cost <= 0)
            return new ChargeResult(ChargeStatus.Charged, await GetBalanceAsync(userId, streamerId, ct));
        if (await ProcessedAsync(operationId, ct))
            return new ChargeResult(ChargeStatus.AlreadyProcessed, await GetBalanceAsync(userId, streamerId, ct));

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var now = DateTimeOffset.UtcNow;
        await EnsureWalletAsync(userId, streamerId, now, ct);

        var balances = await _db.Database
            .SqlQuery<long>($@"UPDATE ""StreamerWallets""
                               SET ""Coins"" = ""Coins"" - {cost}, ""UpdatedAt"" = {now}
                               WHERE ""UserId"" = {userId} AND ""StreamerId"" = {streamerId} AND ""Coins"" >= {cost}
                               RETURNING ""Coins"" AS ""Value""")
            .ToListAsync(ct);

        if (balances.Count == 0)
        {
            await tx.RollbackAsync(ct);
            return new ChargeResult(ChargeStatus.InsufficientFunds, await GetBalanceAsync(userId, streamerId, ct));
        }

        await LogAsync(operationId, userId, streamerId, roomId, reason, -cost, balances[0], ct);
        await tx.CommitAsync(ct);
        return new ChargeResult(ChargeStatus.Charged, balances[0]);
    }

    /// <summary>
    /// Начислить средства на баланс зрителя у стримера (фарминг, выплата ставки,
    /// возврат). Идемпотентно по <paramref name="operationId"/>: повтор с тем же
    /// ключом не начислит дважды. Возвращает баланс после операции
    /// </summary>
    public async Task<long> CreditAsync(Guid operationId, Guid userId, Guid streamerId,
        Guid roomId, string reason, long amount, CancellationToken ct = default)
    {
        if (amount <= 0 || await ProcessedAsync(operationId, ct))
            return await GetBalanceAsync(userId, streamerId, ct);

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var now = DateTimeOffset.UtcNow;
        await EnsureWalletAsync(userId, streamerId, now, ct);

        var balances = await _db.Database
            .SqlQuery<long>($@"UPDATE ""StreamerWallets""
                               SET ""Coins"" = ""Coins"" + {amount}, ""UpdatedAt"" = {now}
                               WHERE ""UserId"" = {userId} AND ""StreamerId"" = {streamerId}
                               RETURNING ""Coins"" AS ""Value""")
            .ToListAsync(ct);

        await LogAsync(operationId, userId, streamerId, roomId, reason, amount, balances[0], ct);
        await tx.CommitAsync(ct);
        return balances[0];
    }

    private Task<bool> ProcessedAsync(Guid operationId, CancellationToken ct)
        => _db.CoinTransactions.AsNoTracking().AnyAsync(t => t.Id == operationId, ct);

    /// <summary>
    /// Гарантирует наличие кошелька (race-safe через уникальный индекс
    /// UserId+StreamerId и ON CONFLICT DO NOTHING)
    /// </summary>
    private Task EnsureWalletAsync(Guid userId, Guid streamerId, DateTimeOffset now, CancellationToken ct)
        => _db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO ""StreamerWallets"" (""Id"", ""UserId"", ""StreamerId"", ""Coins"", ""CreatedAt"", ""UpdatedAt"")
            VALUES ({Guid.NewGuid()}, {userId}, {streamerId}, {StartingBalance}, {now}, {now})
            ON CONFLICT (""UserId"", ""StreamerId"") DO NOTHING", ct);

    private Task LogAsync(Guid operationId, Guid userId, Guid streamerId, Guid roomId,
        string reason, long delta, long balanceAfter, CancellationToken ct)
    {
        _db.CoinTransactions.Add(new CoinTransaction
        {
            Id = operationId,
            UserId = userId,
            StreamerId = streamerId,
            RoomId = roomId,
            EventId = reason,
            Amount = delta,
            BalanceAfter = balanceAfter
        });
        return _db.SaveChangesAsync(ct);
    }
}