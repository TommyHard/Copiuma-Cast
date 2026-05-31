using System.IO;
using System.Text.Json;

namespace Cast.Desktop.Services;

/// <summary>
/// Локальные настройки десктопа с привязкой к аккаунту (по userId). Хранятся в
/// %AppData%\CopiumaCast\settings.json. Сейчас здесь хранится горячая клавиша
/// открытия оверлея; её VK-код дополнительно пишется в файл, который читает
/// нативная DLL оверлея при инъекции (см. Overlay::LoadToggleKey)
/// </summary>
public sealed class DesktopSettings
{
    private const int DefaultHotkeyVk = 0x77; // VK_F8

    private readonly string _path;
    private Dictionary<string, int> _hotkeys = new();

    public DesktopSettings()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CopiumaCast");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "settings.json");
        Load();
    }

    private void Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                var data = JsonSerializer.Deserialize<State>(File.ReadAllText(_path));
                _hotkeys = data?.Hotkeys ?? new();
            }
        }
        catch { _hotkeys = new(); }
    }

    private void Save()
    {
        try { File.WriteAllText(_path, JsonSerializer.Serialize(new State { Hotkeys = _hotkeys })); }
        catch { /* ignore */ }
    }

    /// <summary>VK-код горячей клавиши оверлея для аккаунта (по умолчанию F8)</summary>
    public int GetOverlayHotkey(string? userId)
        => userId is not null && _hotkeys.TryGetValue(userId, out var vk) ? vk : DefaultHotkeyVk;

    /// <summary>Сохранить горячую клавишу для аккаунта и применить для оверлея</summary>
    public void SetOverlayHotkey(string? userId, int vk)
    {
        if (userId is not null) { _hotkeys[userId] = vk; Save(); }
        WriteOverlayHotkeyFile(vk);
    }

    /// <summary>Применить сохранённую клавишу аккаунта (вызывать при логине)</summary>
    public void ApplyOverlayHotkey(string? userId) => WriteOverlayHotkeyFile(GetOverlayHotkey(userId));

    // Пишем VK-код в %TEMP%\CopiumaCast\overlay-hotkey — путь совпадает с тем,
    // что читает DLL оверлея
    private static void WriteOverlayHotkeyFile(int vk)
    {
        try
        {
            var dir = Path.Combine(Path.GetTempPath(), "CopiumaCast");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "overlay-hotkey"), vk.ToString());
        }
        catch { /* ignore */ }
    }

    private sealed class State
    {
        public Dictionary<string, int> Hotkeys { get; set; } = new();
    }
}
