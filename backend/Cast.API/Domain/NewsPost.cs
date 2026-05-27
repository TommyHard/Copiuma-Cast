namespace Cast.API.Domain;

/// <summary>
/// Новость сервиса (Лента на дашборде). Создаётся администратором
/// через конструктор; публикуется флагом Published
/// </summary>
public sealed class NewsPost
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }

    public bool Published { get; set; }

    public Guid AuthorId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
}