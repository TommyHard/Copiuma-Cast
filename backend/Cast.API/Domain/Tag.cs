namespace Cast.API.Domain;

/// <summary>
/// Справочник тегов медиа. Единый источник для подсказок/поиска и создания
/// новых тегов. Имя хранится в нижнем регистре, уникально. Сами присвоенные
/// медиа теги остаются в MediaItem. Tags — эта таблица их каталогизирует
/// </summary>
public sealed class Tag
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Имя тега в нижнем регистре (уникально)
    /// </summary>
    public string Name { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}