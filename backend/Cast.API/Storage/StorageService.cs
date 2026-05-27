using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Util;
using Microsoft.Extensions.Options;

namespace Cast.API.Storage;

/// <summary>
/// Доступ к объектному хранилищу. Аватары — в публичном бакете (прямые ссылки).
/// Медиа — в приватном бакете: загрузка серверная, чтение только через временные
/// presigned-ссылки (медиа не выкладывается в открытый доступ)
/// </summary>
public sealed class StorageService
{
    private readonly StorageOptions _options;
    private readonly IAmazonS3 _client;
    private readonly ILogger<StorageService> _logger;
    private readonly SemaphoreSlim _initGate = new(1, 1);
    private bool _ready;

    public StorageService(IOptions<StorageOptions> options, ILogger<StorageService> logger)
    {
        _options = options.Value;
        _logger = logger;
        _client = new AmazonS3Client(_options.AccessKey, _options.SecretKey, new AmazonS3Config
        {
            ServiceURL = _options.Endpoint,
            ForcePathStyle = true
        });
    }

    /// <summary>
    /// Загрузить аватар в публичный бакет, вернуть прямой URL
    /// </summary>
    public async Task<string> UploadPublicAsync(Stream content, string key, string contentType, CancellationToken ct = default)
    {
        await EnsureBucketsAsync(ct);
        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _options.PublicBucket,
            Key = key,
            InputStream = content,
            ContentType = contentType,
            AutoCloseStream = false
        }, ct);
        return $"{_options.PublicBaseUrl.TrimEnd('/')}/{_options.PublicBucket}/{key}";
    }

    /// <summary>
    /// Загрузить медиа в приватный бакет (возвращает ключ)
    /// </summary>
    public async Task<string> UploadMediaAsync(Stream content, string key, string contentType, CancellationToken ct = default)
    {
        await EnsureBucketsAsync(ct);
        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _options.MediaBucket,
            Key = key,
            InputStream = content,
            ContentType = contentType,
            AutoCloseStream = false
        }, ct);
        return key;
    }

    /// <summary>
    /// Удалить файл из публичного бакета
    /// </summary>
    public async Task DeletePublicAsync(string key, CancellationToken ct = default)
    {
        try
        {
            await _client.DeleteObjectAsync(_options.PublicBucket, key, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при удалении {Key} из MinIO", key);
        }
    }

    /// <summary>
    /// Временная presigned-ссылка на приватный объект медиа
    /// </summary>
    public string PresignMedia(string? key)
    {
        if (string.IsNullOrEmpty(key))
            return string.Empty;

        var url = _client.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = _options.MediaBucket,
            Key = key,
            Verb = HttpVerb.GET,
            Expires = DateTime.UtcNow.AddMinutes(_options.PresignMinutes),
            Protocol = Protocol.HTTP
        });

        var publicBase = _options.PublicBaseUrl.TrimEnd('/');

        url = url.Replace("https://minio:9000", publicBase)
                 .Replace("http://minio:9000", publicBase);

        return url;
    }

    private async Task EnsureBucketsAsync(CancellationToken ct)
    {
        if (_ready) return;
        await _initGate.WaitAsync(ct);
        try
        {
            if (_ready) return;

            // Приватный бакет — без публичной политики
            if (!await AmazonS3Util.DoesS3BucketExistV2Async(_client, _options.MediaBucket))
                await _client.PutBucketAsync(_options.MediaBucket, ct);

            // Публичный бакет аватаров — с public-read
            if (!await AmazonS3Util.DoesS3BucketExistV2Async(_client, _options.PublicBucket))
                await _client.PutBucketAsync(_options.PublicBucket, ct);
            var policy =
                "{\"Version\":\"2012-10-17\",\"Statement\":[{\"Effect\":\"Allow\"," +
                "\"Principal\":\"*\",\"Action\":[\"s3:GetObject\"]," +
                $"\"Resource\":[\"arn:aws:s3:::{_options.PublicBucket}/*\"]}}]}}";
            await _client.PutBucketPolicyAsync(_options.PublicBucket, policy, ct);

            _ready = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось подготовить бакеты.");
            throw;
        }
        finally { _initGate.Release(); }
    }
}