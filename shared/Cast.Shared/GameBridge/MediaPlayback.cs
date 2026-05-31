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

    /// <summary>
    /// Ник отправителя (для подписи в оверлее)
    /// </summary>
    public string SenderName { get; set; } = string.Empty;

    /// <summary>
    /// Стоимость отправки (для подписи в оверлее)
    /// </summary>
    public long Cost { get; set; }

    /// <summary>
    /// true — видео (есть картинка), false — только звук
    /// </summary>
    public bool IsVideo { get; set; }

    public int? ClipStartMs { get; set; }
    public int? ClipEndMs { get; set; }
    public int PosXPct { get; set; } = 50;
    public int PosYPct { get; set; } = 50;
    public int ScalePct { get; set; } = 100;

    /// <summary>
    /// Идентификатор списания (для возврата баллов, если воспроизведение не
    /// удалось). null, если списания не было (например, у владельца)
    /// </summary>
    public string? ChargeId { get; set; }

    public MediaPlayback() { }
}