using System.Collections.Concurrent;

namespace Cast.API.Realtime;

/// <summary>
/// Перезарядка событий: не даёт зрителю спамить одним и тем же событием чаще,
/// чем задано cooldown в манифесте. Проверяется ДО списания валюты, чтобы спам
/// не сжигал баланс и не слал бесполезные команды.
///
/// Хранилище в памяти (на узел). При нескольких узлах с Redis-backplane
/// перезарядка действует пер-узел — этого достаточно, чтобы остановить спам с
/// одного клиента; при необходимости позже можно вынести в Redis.
/// </summary>
public sealed class EventCooldownService
{
    private readonly ConcurrentDictionary<(Guid roomId, Guid userId, string eventId), long> _lastTriggerTicks = new();

    /// <summary>
    /// Проверяет и (при успехе) фиксирует срабатывание. Возвращает true и 0, если
    /// событие можно запускать; иначе false и сколько миллисекунд осталось ждать.
    /// cooldownMs &lt;= 0 — перезарядки нет
    /// </summary>
    public (bool allowed, long remainingMs) TryTrigger(Guid roomId, Guid userId, string eventId, int cooldownMs)
    {
        if (cooldownMs <= 0)
            return (true, 0);

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var key = (roomId, userId, eventId);

        while (true)
        {
            if (_lastTriggerTicks.TryGetValue(key, out var last))
            {
                var elapsed = now - last;
                if (elapsed < cooldownMs)
                    return (false, cooldownMs - elapsed);

                if (_lastTriggerTicks.TryUpdate(key, now, last))
                    return (true, 0);
                // Гонка: значение поменялось — перечитываем и проверяем снова
                continue;
            }

            if (_lastTriggerTicks.TryAdd(key, now))
                return (true, 0);
        }
    }
}
