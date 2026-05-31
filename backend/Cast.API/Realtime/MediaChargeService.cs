using System.Collections.Concurrent;

namespace Cast.API.Realtime;

/// <summary>
/// Реестр списаний за отправленное медиа — чтобы вернуть баллы, если медиа не
/// воспроизвелось у стримера (оверлей сообщает об ошибке). Хранение в памяти:
/// возврат имеет смысл только в рамках живой сессии. Каждое списание берётся
/// ровно один раз (идемпотентность возврата)
/// </summary>
public sealed class MediaChargeService
{
    public readonly record struct Charge(Guid UserId, Guid StreamerId, Guid RoomId, long Cost);

    private readonly ConcurrentDictionary<Guid, Charge> _pending = new();

    public void Register(Guid chargeId, Guid userId, Guid streamerId, Guid roomId, long cost)
    {
        if (cost > 0)
            _pending[chargeId] = new Charge(userId, streamerId, roomId, cost);
    }

    /// <summary>
    /// Забрать списание для возврата (один раз). false — уже возвращено/нет
    /// </summary>
    public bool TryConsume(Guid chargeId, out Charge charge) => _pending.TryRemove(chargeId, out charge);
}
