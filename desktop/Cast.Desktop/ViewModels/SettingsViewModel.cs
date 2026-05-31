using Cast.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows.Input;

namespace Cast.Desktop.ViewModels;

/// <summary>
/// Настройки десктопа. Сейчас — переназначение горячей клавиши открытия оверлея
/// (запоминается на аккаунт и применяется к нативному оверлею)
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly DesktopSettings _settings;
    private readonly string? _userId;

    [ObservableProperty] private string _overlayHotkeyText = "F8";
    [ObservableProperty] private bool _capturing;

    public SettingsViewModel(DesktopSettings settings, string? userId)
    {
        _settings = settings;
        _userId = userId;
        var vk = _settings.GetOverlayHotkey(_userId);
        OverlayHotkeyText = KeyInterop.KeyFromVirtualKey(vk).ToString();
    }

    /// <summary>Начать перехват следующей нажатой клавиши</summary>
    public void BeginCapture() => Capturing = true;

    /// <summary>
    /// Применить захваченную клавишу: сохраняет VK для аккаунта и пишет в конфиг
    /// оверлея. Модификаторы и Esc игнорируем
    /// </summary>
    public void CaptureKey(Key key)
    {
        if (!Capturing) return;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt or Key.System or Key.Escape)
            return;

        var vk = KeyInterop.VirtualKeyFromKey(key);
        _settings.SetOverlayHotkey(_userId, vk);
        OverlayHotkeyText = key.ToString();
        Capturing = false;
        AppLog.Info($"Горячая клавиша оверлея: {key} (VK={vk}). Применится при следующем запуске игры.");
    }
}
