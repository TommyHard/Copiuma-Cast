using System.Collections.Concurrent;

namespace Cast.API.Realtime;

/// <summary>
/// Готовность игрового моста стримера в комнате. Десктоп периодически сообщает,
/// подключён ли мост к игре (мод принимает команды). Пока мост не готов, сервер
/// НЕ принимает события зрителей и НЕ списывает баллы
///
/// Значение живёт с небольшим TTL: если десктоп перестал слать heartbeat
/// (завис/закрылся), готовность считается просроченной и события отклоняются
/// </summary>
public sealed class BridgeReadinessService
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(20);

    private readonly ConcurrentDictionary<Guid, (bool ready, DateTimeOffset at)> _state = new();

    /// <summary>
    /// Десктоп сообщил состояние моста для комнаты
    /// </summary>
    public void Report(Guid roomId, bool ready)
        => _state[roomId] = (ready, DateTimeOffset.UtcNow);

    /// <summary>
    /// Готов ли мост принимать события (свежий heartbeat + ready)
    /// </summary>
    public bool IsReady(Guid roomId)
        => _state.TryGetValue(roomId, out var s)
           && s.ready
           && DateTimeOffset.UtcNow - s.at <= Ttl;

    public void Clear(Guid roomId) => _state.TryRemove(roomId, out _);
}
