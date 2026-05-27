using Cast.API.Domain;

namespace Cast.API.Media;

public sealed record MediaDto(
    Guid Id,
    Guid OwnerId,
    string Title,
    MediaType Type,
    MediaStatus Status,
    IReadOnlyList<string> Tags,
    long CostCoins,
    string OriginalUrl,
    string? WebmUrl,
    string? OggUrl,
    int? DurationMs,
    int? ClipStartMs,
    int? ClipEndMs,
    int PosXPct,
    int PosYPct,
    int ScalePct,
    bool Processed,
    DateTimeOffset CreatedAt);

public sealed record ApproveMediaRequest(List<string> Tags, long CostCoins);

/// <summary>
/// Параметры монтажа и позиционирования (редактор медиа)
/// </summary>
public sealed record EditMediaRequest(
    int? ClipStartMs,
    int? ClipEndMs,
    int PosXPct,
    int PosYPct,
    int ScalePct);