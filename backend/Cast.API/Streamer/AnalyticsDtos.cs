namespace Cast.API.Streamer;

public sealed record ViewerStatDto(Guid UserId, string DisplayName, string Handle, string? AvatarUrl, long Spent);
public sealed record EventStatDto(string EventId, long Count);
public sealed record MediaStatDto(Guid MediaId, string Title, long Count);

/// <summary>
/// Дашборд стримера: оборот валюты, топ-зрители, популярные события и
/// медиа — в пределах комнат этого стримера
/// </summary>
public sealed record AnalyticsDto(
    long TurnoverSpent,
    long TurnoverCredited,
    IReadOnlyList<ViewerStatDto> TopViewers,
    IReadOnlyList<EventStatDto> PopularEvents,
    IReadOnlyList<MediaStatDto> PopularMedia);