using System.Windows;
using Cast.Shared.GameBridge;

namespace Cast.Desktop
{
    /// <summary>
    /// Тест-экран GameBridge: загрузить манифест мода, поднять транспорт и
    /// отправлять события в живой мод (GTA SA слушает сокет 14888). Временный
    /// харнесс — позже источником команд станет бэкенд (SignalR)
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly GameBridgeService _bridge = new();

        public MainWindow()
        {
            InitializeComponent();
            _bridge.Log += AppendLog;
            Closed += async (_, _) => await _bridge.DisposeAsync();
        }

        private void AppendLog(string message)
        {
            Dispatcher.Invoke(() =>
            {
                LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
                LogBox.ScrollToEnd();
            });
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Выберите манифест мода",
                Filter = "JSON-манифест (*.json)|*.json|Все файлы (*.*)|*.*"
            };
            if (dlg.ShowDialog() == true)
                ManifestPathBox.Text = dlg.FileName;
        }

        private async void LoadButton_Click(object sender, RoutedEventArgs e)
        {
            var path = ManifestPathBox.Text?.Trim();
            if (string.IsNullOrEmpty(path))
            {
                AppendLog("Укажите путь к манифесту.");
                return;
            }

            try
            {
                await _bridge.LoadAsync(path);
                var m = _bridge.Manifest!;
                EventsList.ItemsSource = m.Events;
                StatusText.Text = $"{m.GameName} · транспорт {m.Transport} · " +
                                  $"{(m.Transport == GameBridgeTransport.Socket ? $"{m.SocketHost}:{m.SocketPort}" : m.FileName)} · " +
                                  $"подключение: {(_bridge.IsTransportAvailable ? "есть" : "лениво")}";
            }
            catch (Exception ex)
            {
                AppendLog($"Ошибка загрузки манифеста: {ex.Message}");
            }
        }

        private async void FireButton_Click(object sender, RoutedEventArgs e)
        {
            if (EventsList.SelectedItem is not GameEventDefinition def)
            {
                AppendLog("Выберите событие в списке.");
                return;
            }

            var command = new GameCommand(def.Id, string.IsNullOrWhiteSpace(UsernameBox.Text)
                ? "TestViewer" : UsernameBox.Text.Trim());

            foreach (var pair in ParseArgs(ArgsBox.Text))
                command.Args[pair.Key] = pair.Value;

            try
            {
                await _bridge.DispatchAsync(command);
            }
            catch (Exception ex)
            {
                AppendLog($"Ошибка отправки: {ex.Message}");
            }
        }

        /// <summary>
        /// Разбор строки "key=value;key=value" в пары
        /// </summary>
        private static IEnumerable<KeyValuePair<string, string>> ParseArgs(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                yield break;
            foreach (var part in text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var idx = part.IndexOf('=');
                if (idx > 0)
                    yield return new KeyValuePair<string, string>(
                        part[..idx].Trim(), part[(idx + 1)..].Trim());
            }
        }
    }
}
