namespace Cast.Desktop.Models;

/// <summary>
/// Локальное состояние установленного мода. Хранится в JSON рядом с приложением
/// </summary>
public sealed class InstalledModState
{
    public Dictionary<string, InstalledMod> Mods { get; set; } = new();
}

public sealed class InstalledMod
{
    public string GameId { get; set; } = string.Empty;
    public string GameDirectory { get; set; } = string.Empty;
    public string ModVersion { get; set; } = string.Empty;
    public DateTime InstalledAt { get; set; }
    public List<InstalledFile> Files { get; set; } = new();
}

public sealed class InstalledFile
{
    public string RelativePath { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
}