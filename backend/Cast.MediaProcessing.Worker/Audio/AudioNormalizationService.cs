using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace Cast.MediaProcessing.Worker.Audio;

/// <summary>
/// Результат нормализации: пути к выходным дорожкам и длительность
/// </summary>
public readonly record struct NormalizationResult(string WebmPath, string OggPath, int DurationMs);

/// <summary>
/// Двухпроходная нормализация громкости по EBU R128 (-16 LUFS) через FFmpeg
/// (фильтр loudnorm) с защитой от скримеров (true-peak лимит + alimiter).
/// Проход 1 измеряет параметры (print_format=json), проход 2 применяет их и
/// кодирует в webm/opus и ogg/vorbis. Требует ffmpeg/ffprobe в PATH
/// </summary>
public sealed class AudioNormalizationService
{
    private const string TargetI = "-16";   // целевая интегральная громкость, LUFS
    private const string TargetTP = "-1.5"; // потолок истинного пика, dBTP
    private const string TargetLRA = "11";  // целевой диапазон громкости, LU

    private readonly ILogger<AudioNormalizationService> _logger;

    public AudioNormalizationService(ILogger<AudioNormalizationService> logger) => _logger = logger;

    public async Task<NormalizationResult> NormalizeAsync(string inputPath, string outDir, int? clipStartMs, int? clipEndMs, CancellationToken ct = default)
    {
        Directory.CreateDirectory(outDir);

        var seek = BuildSeek(clipStartMs, clipEndMs);

        var measured = await MeasureAsync(inputPath, seek, ct);

        var loudnorm =
            $"loudnorm=I={TargetI}:TP={TargetTP}:LRA={TargetLRA}:" +
            $"measured_I={measured.InputI}:measured_TP={measured.InputTP}:" +
            $"measured_LRA={measured.InputLRA}:measured_thresh={measured.InputThresh}:" +
            $"offset={measured.TargetOffset}:linear=true:print_format=summary";
        var filter = $"{loudnorm},alimiter=limit=0.95";

        var webmPath = Path.Combine(outDir, "audio.webm");
        var oggPath = Path.Combine(outDir, "audio.ogg");

        await RunAsync("ffmpeg",
            $"-y -i \"{inputPath}\" {seek}-af \"{filter}\" -c:a libopus -b:a 128k \"{webmPath}\"", ct);
        await RunAsync("ffmpeg",
            $"-y -i \"{inputPath}\" {seek}-af \"{filter}\" -c:a libvorbis -q:a 5 \"{oggPath}\"", ct);

        var durationMs = await ProbeDurationMsAsync(webmPath, ct);

        return new NormalizationResult(webmPath, oggPath, durationMs);
    }

    private readonly record struct LoudnormMeasure(
        string InputI, string InputTP, string InputLRA, string InputThresh, string TargetOffset);

    private async Task<LoudnormMeasure> MeasureAsync(string inputPath, string seek, CancellationToken ct)
    {
        var stderr = await RunAsync("ffmpeg",
            $"-hide_banner -i \"{inputPath}\" {seek}" +
            $"-af loudnorm=I={TargetI}:TP={TargetTP}:LRA={TargetLRA}:print_format=json " +
            $"-f null -", ct);

        var start = stderr.LastIndexOf('{');
        var end = stderr.LastIndexOf('}');
        if (start < 0 || end <= start)
            throw new InvalidOperationException("Не удалось разобрать вывод loudnorm (проход 1).");

        using var doc = JsonDocument.Parse(stderr[start..(end + 1)]);
        var r = doc.RootElement;
        return new LoudnormMeasure(
            r.GetProperty("input_i").GetString()!,
            r.GetProperty("input_tp").GetString()!,
            r.GetProperty("input_lra").GetString()!,
            r.GetProperty("input_thresh").GetString()!,
            r.GetProperty("target_offset").GetString()!);
    }

    private static string BuildSeek(int? startMs, int? endMs)
    {
        var sb = new System.Text.StringBuilder();
        if (startMs is > 0)
            sb.Append($"-ss {(startMs.Value / 1000.0).ToString(CultureInfo.InvariantCulture)} ");
        if (endMs is > 0)
            sb.Append($"-to {(endMs.Value / 1000.0).ToString(CultureInfo.InvariantCulture)} ");
        return sb.ToString();
    }

    private async Task<int> ProbeDurationMsAsync(string inputPath, CancellationToken ct)
    {
        var output = await RunAsync("ffprobe",
            $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{inputPath}\"", ct);
        var text = output.Trim();
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
            ? (int)(seconds * 1000)
            : 0;
    }

    /// <summary>
    /// Запускает процесс и возвращает stdout (для ffprobe) либо stderr (ffmpeg
    /// пишет диагностику и JSON loudnorm в stderr). Бросает при ненулевом коде
    /// </summary>
    private async Task<string> RunAsync(string fileName, string arguments, CancellationToken ct)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (process.ExitCode != 0)
        {
            _logger.LogError("{Tool} завершился с кодом {Code}: {Err}", fileName, process.ExitCode, stderr);
            throw new InvalidOperationException($"{fileName} вернул код {process.ExitCode}.");
        }

        return string.IsNullOrWhiteSpace(stdout) ? stderr : stdout;
    }
}