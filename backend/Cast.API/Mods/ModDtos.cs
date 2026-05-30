namespace Cast.API.Mods;

/// <summary>
/// Информация о пакете мода для десктоп-клиента: файлы для скачивания
/// с контрольными суммами. Источник — манифест + описание файлов в JSON-конфиге
/// </summary>
public sealed class ModPackageDto
{
    public string GameId { get; set; } = string.Empty;
    public string GameName { get; set; } = string.Empty;
    public string ModVersion { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>
    /// Прямая ссылка на .zip-архив мода (скачивается и распаковывается целиком)
    /// </summary>
    public string ArchiveUrl { get; set; } = string.Empty;

    public List<ModFileDto> Libraries { get; set; } = new();
    public List<ModFileDto> ModFiles { get; set; } = new();
    public ModFileDto? Manifest { get; set; }
}

public sealed class ModFileDto
{
    public string RelativePath { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public long Size { get; set; }
}