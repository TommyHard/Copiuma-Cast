using Cast.MediaProcessing.Worker.Audio;
using Cast.MediaProcessing.Worker.Data;
using Cast.MediaProcessing.Worker.Domain;
using Cast.MediaProcessing.Worker.Storage;
using Microsoft.EntityFrameworkCore;

namespace Cast.MediaProcessing.Worker.Processing;

/// <summary>
/// Фоновая аудио-обработка как отдельный микросервис: берёт необработанные
/// звуки из общей БД, скачивает оригинал из MinIO, прогоняет двухпроходную
/// нормализацию (EBU R128) и загружает webm/ogg обратно. При сбое фиксирует
/// ProcessingError и не зацикливается. Модерация (в Cast.API) идёт независимо
/// </summary>
public sealed class MediaProcessingService : BackgroundService
{
    private const int IntervalSeconds = 15;
    private const int BatchSize = 3;

    private readonly IServiceProvider _services;
    private readonly MinioStorage _storage;
    private readonly AudioNormalizationService _normalizer;
    private readonly ILogger<MediaProcessingService> _logger;

    public MediaProcessingService(
        IServiceProvider services,
        MinioStorage storage,
        AudioNormalizationService normalizer,
        ILogger<MediaProcessingService> logger)
    {
        _services = services;
        _storage = storage;
        _normalizer = normalizer;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(IntervalSeconds));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await ProcessBatchAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Сбой цикла обработки медиа.");
            }
        }
    }
    
    private async Task ProcessBatchAsync(CancellationToken ct)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediaDbContext>();

        var batch = await db.MediaItems
            .Where(m => m.Status == MediaStatus.Approved && !m.Processed && m.ProcessingError == null)
            .OrderBy(m => m.Id)
            .Take(BatchSize)
            .ToListAsync(ct);

        foreach (var item in batch)
            await ProcessOneAsync(db, item, ct);
    }

    private async Task ProcessOneAsync(MediaDbContext db, MediaItem item, CancellationToken ct)
    {
        var workDir = Path.Combine(Path.GetTempPath(), $"cast-media-{item.Id:N}");
        Directory.CreateDirectory(workDir);
        var inputPath = Path.Combine(workDir, "input" + Path.GetExtension(item.OriginalKey));

        try
        {
            await _storage.DownloadToFileAsync(item.OriginalKey, inputPath, ct);

            if (item.Type == MediaType.Sound)
            {
                var result = await _normalizer.NormalizeAsync(inputPath, workDir, item.ClipStartMs, item.ClipEndMs, ct);

                var webmKey = $"media/sound/{item.OwnerId}/{item.Id:N}.webm";
                var oggKey = $"media/sound/{item.OwnerId}/{item.Id:N}.ogg";
                await _storage.UploadFileAsync(result.WebmPath, webmKey, "audio/webm", ct);
                await _storage.UploadFileAsync(result.OggPath, oggKey, "audio/ogg", ct);
                item.WebmKey = webmKey;
                item.OggKey = oggKey;
                item.DurationMs = result.DurationMs;
            }
            else if (item.Type == MediaType.Video)
            {
                var seek = "";
                if (item.ClipStartMs is > 0) seek += $"-ss {item.ClipStartMs.Value / 1000.0} ";
                if (item.ClipEndMs is > 0) seek += $"-to {item.ClipEndMs.Value / 1000.0} ";

                var outVideoPath = Path.Combine(workDir, "output.webm");

                // Перекодируем видео в WebM (VP9 + Opus): эти кодеки умеет
                // воспроизводить CEF-оверлей. H.264/AAC у стандартного CEF нет
                // (нет проприетарных кодеков)
                var ffmpegArgs = $"-y -i \"{inputPath}\" {seek}-c:v libvpx-vp9 -b:v 0 -crf 33 " +
                                 $"-deadline realtime -cpu-used 5 -row-mt 1 -c:a libopus -b:a 128k \"{outVideoPath}\"";

                await ExecuteFfmpegAsync(ffmpegArgs, ct);

                var videoKey = $"media/video/{item.OwnerId}/{item.Id:N}.webm";
                await _storage.UploadFileAsync(outVideoPath, videoKey, "video/webm", ct);

                // WebmKey — то, что отдаём оверлею; OriginalKey оставляем как исходник
                item.WebmKey = videoKey;
            }

            item.Processed = true;
            await db.SaveChangesAsync(ct);
            _logger.LogInformation("Медиа {Id} ({Type}) успешно обработано.", item.Id, item.Type);
        }
        catch (Exception ex)
        {
            item.ProcessingError = ex.Message.Length > 1000 ? ex.Message[..1000] : ex.Message;
            await db.SaveChangesAsync(ct);
            _logger.LogError(ex, "Не удалось обработать медиа {Id}.", item.Id);
        }
        finally
        {
            try { Directory.Delete(workDir, recursive: true); } catch { /* ignore */ }
        }
    }

    private async Task ExecuteFfmpegAsync(string arguments, CancellationToken ct)
    {
        using var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        process.Start();
        await process.WaitForExitAsync(ct);
        if (process.ExitCode != 0) throw new InvalidOperationException("Ошибка кодирования видео FFmpeg.");
    }
}