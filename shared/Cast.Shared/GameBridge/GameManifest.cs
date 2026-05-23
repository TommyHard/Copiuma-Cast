namespace Cast.Shared.GameBridge;

/// <summary>
/// Транспорт, которым десктоп общается с модом игры
/// </summary>
public enum GameBridgeTransport
{
    /// <summary>
    /// Loopback-сокет к моду (предпочтительно; GTA SA)
    /// </summary>
    Socket,
    /// <summary>
    /// Команды дописываются в файл, мод их вычитывает
    /// </summary>
    File,
    /// <summary>
    /// Прямая правка памяти процесса игры (на будущее)
    /// </summary>
    Memory
}

/// <summary>
/// Манифест мода игры — единый контракт между модом, десктопом, бэкендом и
/// интерфейсом зрителя. Лежит рядом с модом (JSON), читается десктопом и
/// публикуется бэкендом, чтобы все видели доступные события. Является «белым
/// списком»: вызвать можно только перечисленные здесь события
/// </summary>
public sealed class GameManifest
{
    /// <summary>
    /// Версия схемы манифеста (для совместимости)
    /// </summary>
    public int ManifestVersion { get; set; } = 1;

    /// <summary>
    /// Стабильный идентификатор игры, напр. "gta_sa"
    /// </summary>
    public string GameId { get; set; } = string.Empty;

    /// <summary>
    /// Отображаемое название игры/мода
    /// </summary>
    public string GameName { get; set; } = string.Empty;

    /// <summary>
    /// Версия самого мода (информативно)
    /// </summary>
    public string? ModVersion { get; set; }

    /// <summary>
    /// Предпочитаемый транспорт связи с модом
    /// </summary>
    public GameBridgeTransport Transport { get; set; } = GameBridgeTransport.Socket;

    /// <summary>
    /// Хост сокет-транспорта (по умолчанию loopback)
    /// </summary>
    public string SocketHost { get; set; } = "127.0.0.1";

    /// <summary>
    /// Порт сокет-транспорта (мод GTA SA: 14888)
    /// </summary>
    public int SocketPort { get; set; } = 14888;

    /// <summary>
    /// Имя файла-моста (рядом с модом) для файлового транспорта
    /// </summary>
    public string FileName { get; set; } = "events.txt";

    /// <summary>
    /// Доступные события (белый список)
    /// </summary>
    public List<GameEventDefinition> Events { get; set; } = new();
}