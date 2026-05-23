using System.Text;
using Cast.Shared.GameBridge;

namespace Cast.GameBridge.Transports;

/// <summary>
/// Запасной транспорт для игр без сокета: команды дописываются в файл-мост
/// (по умолчанию events.txt) рядом с модом. Мод периодически вычитывает строки
/// (io.lines) и очищает файл. Мы только дописываем — это совпадает с поведением
/// текущего event.lua. Доступ сериализуется, чтобы строки не перемешивались
/// </summary>
public sealed class FileGameBridge : IGameBridge
{
    private readonly string _filePath;
    private readonly string _lineTerminator;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public FileGameBridge(string filePath, string lineTerminator = "\n")
    {
        _filePath = filePath;
        _lineTerminator = lineTerminator;
    }

    public GameBridgeTransport Transport => GameBridgeTransport.File;

    /// <summary>
    /// Готов, если каталог назначения существует
    /// </summary>
    public bool IsAvailable
    {
        get
        {
            var dir = Path.GetDirectoryName(_filePath);
            return string.IsNullOrEmpty(dir) || Directory.Exists(dir);
        }
    }

    public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;

    public async Task SendAsync(string commandLine, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var fs = new FileStream(_filePath, FileMode.Append,
                FileAccess.Write, FileShare.ReadWrite);
            var bytes = Encoding.UTF8.GetBytes(commandLine + _lineTerminator);
            await fs.WriteAsync(bytes, ct).ConfigureAwait(false);
            await fs.FlushAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        _gate.Dispose();
        return ValueTask.CompletedTask;
    }
}
