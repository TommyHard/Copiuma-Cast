namespace Cast.Shared.GameBridge;

/// <summary>
/// Тип параметра события. Используется и для валидации на стороне моста,
/// и для авто-генерации поля ввода в интерфейсе зрителя
/// </summary>
public enum GameEventParamType
{
    String,
    Int,
    Float,
    Bool,
    /// <summary>
    /// Значение из фиксированного списка <see cref="GameEventParam.EnumValues"/>
    /// </summary>
    Enum
}

/// <summary>
/// Описание одного параметра события игры (часть манифеста).
/// Сейчас события мода GTA SA беспараметрические, но контракт заложен на
/// будущее: параметризованные события (модель машины, id погоды и т.п.)
/// </summary>
public sealed class GameEventParam
{
    /// <summary>
    /// Машинное имя параметра (ключ в команде), напр. "modelId"
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Человекочитаемое имя для интерфейса зрителя
    /// </summary>
    public string? Title { get; set; }

    public string? Description { get; set; }

    public GameEventParamType Type { get; set; } = GameEventParamType.String;

    public bool Required { get; set; } = true;

    /// <summary>
    /// Значение по умолчанию (как строка; интерпретируется по Type)
    /// </summary>
    public string? Default { get; set; }

    /// <summary>
    /// Мин/макс для числовых типов (включительно). null — без ограничения
    /// </summary>
    public double? Min { get; set; }
    public double? Max { get; set; }

    /// <summary>
    /// Допустимые значения для <see cref="GameEventParamType.Enum"/>
    /// </summary>
    public List<string>? EnumValues { get; set; }
}
