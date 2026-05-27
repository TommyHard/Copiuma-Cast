namespace Cast.API.Domain;

/// <summary>
/// Переопределение доступности события в конкретной комнате/сессии. 
/// Отсутствие строки = событие доступно по умолчанию из манифеста; 
/// строка с Enabled=false выключает его на время этой комнаты
/// </summary>
public sealed class RoomEventToggle
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid RoomId { get; set; }

    /// <summary>
    /// Идентификатор события из манифеста
    /// </summary>
    public string EventId { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;
}