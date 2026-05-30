namespace Cast.API.Domain;

public enum MediaType { Sound = 0, Video = 1 }

public enum MediaStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2,
    /// <summary>
    /// Доступ приостановлен админом: использовать нельзя, но не удалено
    /// </summary>
    Suspended = 3
}

/// <summary>
/// Единица пользовательского медиа-контента. Файлы лежат в ПРИВАТНОМ бакете;
/// доступ — только по временным presigned-ссылкам, генерируемым на чтение
/// (медиа не публично). Здесь хранятся ключи объектов, монтаж (clip) и позиция
/// наложения для оверлея
/// </summary>
public sealed class MediaItem
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid OwnerId { get; set; }

    public string Title { get; set; } = string.Empty;
    public MediaType Type { get; set; }
    public MediaStatus Status { get; set; } = MediaStatus.Pending;

    public List<string> Tags { get; set; } = new();

    /// <summary>
    /// Стоимость воспроизведения; назначает админ при одобрении
    /// </summary>
    public long CostCoins { get; set; }

    /// <summary>
    /// Ключи объектов в приватном бакете
    /// </summary>
    public string OriginalKey { get; set; } = string.Empty;
    public string? WebmKey { get; set; }
    public string? OggKey { get; set; }

    public int? DurationMs { get; set; }

    // --- Монтаж и позиционирование (редактор медиа) ---

    /// <summary>
    /// Начало/конец фрагмента в мс (null — без обрезки)
    /// </summary>
    public int? ClipStartMs { get; set; }
    public int? ClipEndMs { get; set; }

    /// <summary>
    /// Позиция центра наложения в % от ширины/высоты экрана
    /// </summary>
    public int PosXPct { get; set; } = 50;
    public int PosYPct { get; set; } = 50;

    /// <summary>
    /// Масштаб наложения в % (для видео)
    /// </summary>
    public int ScalePct { get; set; } = 100;

    public bool Processed { get; set; }
    public string? ProcessingError { get; set; }

    public Guid? ReviewedBy { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}