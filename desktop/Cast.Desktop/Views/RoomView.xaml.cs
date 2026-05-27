using Cast.Desktop.ViewModels;
using System.Windows.Controls;

namespace Cast.Desktop.Views;

public partial class RoomView : UserControl
{
    public RoomView() => InitializeComponent();

    private async void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is RoomViewModel vm)
            await vm.LoadGamesCommand.ExecuteAsync(null);
    }
}