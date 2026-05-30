namespace Cast.Desktop.Models;

/// <summary>
/// Информация о моде с сервера платформы: что можно скачать и установить
/// </summary>
public sealed class ModPackageInfo
{
    public string GameId { get; set; } = string.Empty;
    public string GameName { get; set; } = string.Empty;
    public string ModVersion { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>
    /// Прямая ссылка на .zip-архив мода (скачивается и распаковывается целиком)
    /// </summary>
    public string ArchiveUrl { get; set; } = string.Empty;

    /// <summary>
    /// Файлы библиотек (зависимостей), устанавливаемые отдельно
    /// </summary>
    public List<ModFileEntry> Libraries { get; set; } = new();

    /// <summary>
    /// Файлы самого мода
    /// </summary>
    public List<ModFileEntry> ModFiles { get; set; } = new();

    /// <summary>
    /// Файл манифеста (manifest.json)
    /// </summary>
    public ModFileEntry? Manifest { get; set; }
}

public sealed class ModFileEntry
{
    /// <summary>
    /// Путь относительно директории игры
    /// </summary>
    public string RelativePath { get; set; } = string.Empty;

    /// <summary>
    /// SHA-256 хеш файла для проверки целостности
    /// </summary>
    public string Sha256 { get; set; } = string.Empty;

    /// <summary>
    /// Размер файла в байтах
    /// </summary>
    public long Size { get; set; }
}