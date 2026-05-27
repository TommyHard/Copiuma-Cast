namespace Cast.MediaProcessing.Worker.Storage;

/// <summary>
/// Настройки доступа к объектному хранилищу (MinIO) для воркера
/// </summary>
public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>
    /// Хост:порт без схемы (для MinIO SDK)
    /// </summary>
    public string Endpoint { get; set; } = "localhost:9000";
    public bool UseSsl { get; set; }

    public string AccessKey { get; set; } = "cast";
    public string SecretKey { get; set; } = "cast12345";
    public string Bucket { get; set; } = "cast-media";
}