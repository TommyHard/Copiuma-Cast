using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;

namespace Cast.MediaProcessing.Worker.Storage;

/// <summary>
/// Доступ воркера к MinIO через официальный SDK: скачивание оригинала и
/// загрузка нормализованных дорожек. Клиент потокобезопасен — синглтон
/// </summary>
public sealed class MinioStorage
{
    private readonly IMinioClient _client;
    private readonly StorageOptions _options;

    public MinioStorage(IOptions<StorageOptions> options)
    {
        _options = options.Value;
        _client = new MinioClient()
            .WithEndpoint(_options.Endpoint)
            .WithCredentials(_options.AccessKey, _options.SecretKey)
            .WithSSL(_options.UseSsl)
            .Build();
    }

    public Task DownloadToFileAsync(string key, string destPath, CancellationToken ct = default)
        => _client.GetObjectAsync(new GetObjectArgs()
            .WithBucket(_options.Bucket)
            .WithObject(key)
            .WithFile(destPath), ct);

    public async Task UploadFileAsync(string filePath, string key, string contentType, CancellationToken ct = default)
    {
        await _client.PutObjectAsync(new PutObjectArgs()
            .WithBucket(_options.Bucket)
            .WithObject(key)
            .WithFileName(filePath)
            .WithContentType(contentType), ct);
    }
}