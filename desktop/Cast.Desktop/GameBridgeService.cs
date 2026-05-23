using Cast.GameBridge;
using Cast.Shared.GameBridge;
using System.IO;

namespace Cast.Desktop;

/// <summary>
/// Обёртка над <see cref="GameBridgeManager"/> для десктоп-приложения: держит
/// загруженный манифест и активный мост на время сессии игры, отдаёт лог-события
/// в UI. Позже сюда же придёт приём <see cref="GameCommand"/> из бэкенда (SignalR)
/// </summary>
public sealed class GameBridgeService : IAsyncDisposable
{
    private GameBridgeManager? _manager;

    /// <summary>
    /// Сообщения для отображения в логе UI
    /// </summary>
    public event Action<string>? Log;

    public GameManifest? Manifest => _manager?.Manifest;
    public bool IsReady => _manager is not null;
    public bool IsTransportAvailable => _manager?.Transport.IsAvailable ?? false;

    /// <summary>
    /// Загружает манифест и поднимает транспорт. modDirectory для файлового
    /// транспорта берётся из расположения манифеста
    /// </summary>
    public async Task LoadAsync(string manifestPath, CancellationToken ct = default)
    {
        await DisposeManagerAsync().ConfigureAwait(false);

        var manifest = await ManifestLoader.LoadAsync(manifestPath, ct).ConfigureAwait(false);
        var modDir = Path.GetDirectoryName(manifestPath);
        _manager = GameBridgeManager.Create(manifest, modDir);

        Log?.Invoke($"Манифест загружен: {manifest.GameName} " +
                    $"({manifest.Events.Count} событий, транспорт {manifest.Transport}).");

        await _manager.StartAsync(ct).ConfigureAwait(false);
        Log?.Invoke(_manager.Transport.IsAvailable
            ? "Транспорт подключён."
            : "Транспорт пока не подключён — подключимся при первой отправке.");
    }

    /// <summary>
    /// Отправить команду моду (валидация по манифесту внутри менеджера)
    /// </summary>
    public async Task<DispatchResult> DispatchAsync(GameCommand command, CancellationToken ct = default)
    {
        if (_manager is null)
        {
            Log?.Invoke("Мост не инициализирован — сначала загрузите манифест.");
            return new DispatchResult(DispatchStatus.TransportError, "Мост не инициализирован.");
        }

        var result = await _manager.DispatchAsync(command, ct).ConfigureAwait(false);
        Log?.Invoke(result.Ok
            ? $"OK: {command.EventId} (от {command.Username})"
            : $"Отклонено [{result.Status}]: {result.Reason}");
        return result;
    }

    private async Task DisposeManagerAsync()
    {
        if (_manager is not null)
        {
            await _manager.DisposeAsync().ConfigureAwait(false);
            _manager = null;
        }
    }

    public async ValueTask DisposeAsync() => await DisposeManagerAsync().ConfigureAwait(false);
}
