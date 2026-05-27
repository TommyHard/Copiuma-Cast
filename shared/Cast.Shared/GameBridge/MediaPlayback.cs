namespace Cast.Shared.GameBridge;

/// <summary>
/// Медиа, прикреплённое к событию для воспроизведения в оверлее/на стриме.
/// Передаётся в составе <see cref="GameCommand"/>. Ссылки — временные
/// (presigned), плюс параметры монтажа (clip) и позиционирования наложения
/// </summary>
public sealed class MediaPlayback
{
    public string Title { get; set; } = string.Empty;
    public string? WebmUrl { get; set; }
    public string? OggUrl { get; set; }

    public int? ClipStartMs { get; set; }
    public int? ClipEndMs { get; set; }
    public int PosXPct { get; set; } = 50;
    public int PosYPct { get; set; } = 50;
    public int ScalePct { get; set; } = 100;

    public MediaPlayback() { }
}