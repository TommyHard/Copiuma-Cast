using Cast.Shared.GameBridge;

namespace Cast.GameBridge;

/// <summary>
/// Транспорт связи десктопа с модом игры. Реализации: сокет (основной),
/// файл (запасной), память (на будущее). Один экземпляр живёт на время сессии
/// игры; <see cref="GameBridgeManager"/> выбирает реализацию по манифесту
/// </summary>
public interface IGameBridge : IAsyncDisposable
{
    /// <summary>
    /// Тип транспорта (для диагностики/логов)
    /// </summary>
    GameBridgeTransport Transport { get; }

    /// <summary>
    /// Готов ли транспорт принимать команды (например, сокет подключён).
    /// Файловый транспорт обычно готов всегда
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Поднять транспорт (подключиться к сокету / проверить путь файла)
    /// </summary>
    Task StartAsync(CancellationToken ct = default);

    /// <summary>
    /// Отправить уже сформированную строку-команду моду. Формат строки задаёт
    /// <see cref="ICommandFormatter"/>; транспорт отвечает только за доставку
    /// </summary>
    Task SendAsync(string commandLine, CancellationToken ct = default);
}
