using System.Collections.Concurrent;

namespace Cast.API.Realtime;

/// <summary>
/// Текущая громкость воспроизведения медиа (0-100). Хранится в памяти
/// и синхронизируется между Cast.Desktop и оверлеем через хаб: при подключении
/// получает актуальное значение
/// </summary>
public sealed class MediaVolumeService
{
    private readonly ConcurrentDictionary<Guid, int> _volumes = new();

    public int Get(Guid roomId) => _volumes.TryGetValue(roomId, out var v) ? v : 100;

    public int Set(Guid roomId, int volume)
    {
        var v = Math.Clamp(volume, 0, 100);
        _volumes[roomId] = v;
        return v;
    }
}