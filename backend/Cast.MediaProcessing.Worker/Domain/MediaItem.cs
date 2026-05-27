namespace Cast.MediaProcessing.Worker.Domain;

public enum MediaType 
{ 
    Sound = 0, 
    Video = 1 
}

public enum MediaStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2
}

/// <summary>
/// Узкая проекция таблицы MediaItems для воркера. Хранит ключи объектов (медиа
/// в приватном бакете) и параметры обрезки (clip)
/// </summary>
public sealed class MediaItem
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public MediaType Type { get; set; }

    public MediaStatus Status { get; set; }

    public string OriginalKey { get; set; } = string.Empty;
    public string? WebmKey { get; set; }
    public string? OggKey { get; set; }
    public int? DurationMs { get; set; }

    public int? ClipStartMs { get; set; }
    public int? ClipEndMs { get; set; }

    public bool Processed { get; set; }
    public string? ProcessingError { get; set; }
}