using Cast.Desktop.ViewModels;
using System.Windows.Controls;

namespace Cast.Desktop.Views;

public partial class DebugView : UserControl
{
    public DebugView() => InitializeComponent();

    private void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is DebugViewModel vm)
            vm.RefreshEventsCommand.Execute(null);
    }
}