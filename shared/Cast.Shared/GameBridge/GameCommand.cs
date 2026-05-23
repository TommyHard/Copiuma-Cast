namespace Cast.Shared.GameBridge;

/// <summary>
/// Команда на вызов события в игре в рантайме. Формируется, когда зритель
/// инициировал событие: бэкенд сопоставляет запрос зрителя с событием манифеста
/// и передаёт десктопу, а тот через мост — моду
/// </summary>
public sealed class GameCommand
{
    /// <summary>
    /// Идентификатор события (должен присутствовать в манифесте)
    /// </summary>
    public string EventId { get; set; } = string.Empty;

    /// <summary>
    /// Ник зрителя-инициатора (мод показывает уведомление)
    /// </summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// Параметры события (имя → значение). Для беспараметрических — пусто
    /// </summary>
    public Dictionary<string, string> Args { get; set; } = new();

    public GameCommand() { }

    public GameCommand(string eventId, string username)
    {
        EventId = eventId;
        Username = username;
    }
}