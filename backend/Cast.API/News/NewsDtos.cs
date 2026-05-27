namespace Cast.API.News;

public sealed record NewsDto(
    Guid Id,
    string Title,
    string Body,
    string? ImageUrl,
    bool Published,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    string AuthorName,
    string? AuthorAvatarUrl);

/// <summary>
/// Конструктор новости (создание/редактирование админом)
/// </summary>
public sealed record SaveNewsRequest(string Title, string Body, string? ImageUrl, bool Published);