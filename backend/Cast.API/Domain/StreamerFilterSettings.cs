namespace Cast.API.Domain;

/// <summary>
/// Режим tolerance-фильтров стримера. Block-list: запрещены перечисленные теги
/// (остальное разрешено). Allow-list: разрешены только перечисленные теги
/// (медиа с любым другим тегом блокируется)
/// </summary>
public enum FilterMode
{
    Blocklist = 0,
    Allowlist = 1
}

/// <summary>
/// Настройки фильтрации стримера (одна строка на стримера). Сам набор тегов
/// хранится в <see cref="StreamerTagFilter"/>; здесь — режим их трактовки
/// </summary>
public sealed class StreamerFilterSettings
{
    /// <summary>
    /// Идентификатор стримера (первичный ключ)
    /// </summary>
    public Guid StreamerId { get; set; }

    public FilterMode Mode { get; set; } = FilterMode.Blocklist;
}