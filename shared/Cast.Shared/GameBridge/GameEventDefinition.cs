namespace Cast.Shared.GameBridge;

/// <summary>
/// Описание одного события игры в манифесте мода. Это запись «белого списка»:
/// зрителю показываются только такие события, и мост отклоняет всё, чего здесь
/// нет. Метаданные (стоимость, кулдаун, категория) питают экономику и UI
/// </summary>
public sealed class GameEventDefinition
{
    /// <summary>
    /// Машинный идентификатор (значение eventid в команде мода)
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Человекочитаемое название для интерфейса зрителя
    /// </summary>
    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>
    /// Группировка в UI, напр. "Транспорт", "Погода", "Оружие"
    /// </summary>
    public string? Category { get; set; }

    /// <summary>
    /// Параметры события (пусто — беспараметрическое)
    /// </summary>
    public List<GameEventParam> Params { get; set; } = new();

    /// <summary>
    /// Стоимость вызова в виртуальной валюте (0 — бесплатно)
    /// </summary>
    public int CostCoins { get; set; }

    /// <summary>
    /// Кулдаун между вызовами этого события, мс (0 — без кулдауна).
    ///</summary>
    public int CooldownMs { get; set; }

    /// <summary>
    /// Включено ли событие по умолчанию (стример может переопределить)
    /// </summary>
    public bool Enabled { get; set; } = true;
}