namespace Cast.Desktop.Services;

/// <summary>
/// Настройки десктоп-клиента. По умолчанию весь трафик идёт через шлюз
/// (Cast.Gateway). В Docker Compose шлюз на :5208, при dotnet run — :5067
/// </summary>
public sealed class DesktopOptions
{
    /// <summary>
    /// Базовый URL шлюза (REST /api, хабы /hubs)
    /// </summary>
    public string GatewayUrl { get; set; } = "http://localhost:5208";

    /// <summary>
    /// URL веб-интерфейса для логина в браузере
    /// </summary>
    public string WebUrl { get; set; } = "http://localhost:3000";

    /// <summary>
    /// Путь к exe CEF-хоста оверлея (Cast.Overlay.Host)
    /// </summary>
    public string OverlayHostPath { get; set; } = "Cast.Overlay.Host.exe";

    /// <summary>
    /// Путь к DLL оверлея, инжектируемой в игру (Cast.Overlay)
    /// </summary>
    public string OverlayDllPath { get; set; } = "Cast.Overlay.dll";

    /// <summary>
    /// URL интерфейса оверлея (React), который грузит CEF-хост
    /// </summary>
    public string OverlayUiUrl { get; set; } = "http://localhost:3000/overlay";
}