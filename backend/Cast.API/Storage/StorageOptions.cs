namespace Cast.API.Storage;

/// <summary>
/// Настройки объектного хранилища (S3-совместимое; локально — MinIO). Два
/// бакета: публичный (аватары, public-read) и приватный (медиа, доступ только
/// по временным presigned-ссылкам)
/// </summary>
public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    public string Endpoint { get; set; } = "http://localhost:9000";

    /// <summary>
    /// Базовый URL для публичных ссылок (аватары)
    /// </summary>
    public string PublicBaseUrl { get; set; } = "http://localhost:9000";

    public string AccessKey { get; set; } = "cast";
    public string SecretKey { get; set; } = "cast12345";

    /// <summary>
    /// Публичный бакет (аватары)
    /// </summary>
    public string PublicBucket { get; set; } = "cast-public";

    /// <summary>
    /// Приватный бакет (медиа)
    /// </summary>
    public string MediaBucket { get; set; } = "cast-media";

    /// <summary>
    /// Срок жизни presigned-ссылок на медиа, минут
    /// </summary>
    public int PresignMinutes { get; set; } = 60;
}