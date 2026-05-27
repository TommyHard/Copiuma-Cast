using Cast.Desktop.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace Cast.Desktop.Views;

public partial class ModManagerView : UserControl
{
    public ModManagerView() => InitializeComponent();

    private void BrowseGameDir_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is ModEntryViewModel entry)
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Выберите директорию игры" };
            if (dlg.ShowDialog() == true)
                entry.GameDirectory = dlg.FolderName;
        }
    }
}