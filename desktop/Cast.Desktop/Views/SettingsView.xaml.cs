using Cast.Desktop.ViewModels;
using System.Windows.Controls;
using System.Windows.Input;

namespace Cast.Desktop.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        UpdateButtonText();
    }

    private SettingsViewModel? Vm => DataContext as SettingsViewModel;

    private void OnCaptureClick(object sender, System.Windows.RoutedEventArgs e)
    {
        Vm?.BeginCapture();
        UpdateButtonText();
        CaptureButton.Focus();
    }

    private void OnCaptureKeyDown(object sender, KeyEventArgs e)
    {
        if (Vm is null || !Vm.Capturing) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        Vm.CaptureKey(key);
        UpdateButtonText();
        e.Handled = true;
    }

    private void UpdateButtonText()
    {
        if (Vm is null) return;
        CaptureButton.Content = Vm.Capturing ? "Нажмите клавишу…" : Vm.OverlayHotkeyText;
    }
}
