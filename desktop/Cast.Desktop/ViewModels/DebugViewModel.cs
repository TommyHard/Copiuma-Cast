using Cast.Shared.GameBridge;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace Cast.Desktop.ViewModels;

/// <summary>
/// VM режима отладки: тестовый запуск событий без зрителей.
/// Использует GameBridgeService напрямую, минуя SignalR и бэкенд
/// </summary>
public sealed partial class DebugViewModel : ObservableObject
{
    private readonly GameBridgeService _bridge;

    [ObservableProperty] private string _username = "TestViewer";
    [ObservableProperty] private string _argsText = string.Empty;
    [ObservableProperty] private ObservableCollection<GameEventDefinition> _events = new();
    [ObservableProperty] private GameEventDefinition? _selectedEvent;
    [ObservableProperty] private ObservableCollection<string> _log = new();
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private bool _isReady;

    public DebugViewModel(GameBridgeService bridge)
    {
        _bridge = bridge;
        _bridge.Log += msg => System.Windows.Application.Current.Dispatcher.Invoke(() =>
            Log.Insert(0, $"[{DateTime.Now:HH:mm:ss}] {msg}"));

        RefreshEvents();
    }

    [RelayCommand]
    private void RefreshEvents()
    {
        Events.Clear();
        if (_bridge.Manifest is not null)
        {
            foreach (var e in _bridge.Manifest.Events)
                Events.Add(e);
            IsReady = true;
            StatusText = $"{_bridge.Manifest.GameName} — {Events.Count} событий";
        }
        else
        {
            IsReady = false;
            StatusText = "Манифест не загружен. Запустите игру на вкладке \"Запуск\".";
        }
    }

    [RelayCommand]
    private async Task FireAsync()
    {
        if (SelectedEvent is null)
        {
            StatusText = "Выберите событие.";
            return;
        }

        var cmd = new GameCommand(SelectedEvent.Id,
            string.IsNullOrWhiteSpace(Username) ? "TestViewer" : Username.Trim());

        foreach (var pair in ParseArgs(ArgsText))
            cmd.Args[pair.Key] = pair.Value;

        try
        {
            await _bridge.DispatchAsync(cmd);
        }
        catch (Exception ex)
        {
            Log.Insert(0, $"[{DateTime.Now:HH:mm:ss}] Ошибка: {ex.Message}");
        }
    }

    private static IEnumerable<KeyValuePair<string, string>> ParseArgs(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) yield break;
        foreach (var part in text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var idx = part.IndexOf('=');
            if (idx > 0)
                yield return new(part[..idx].Trim(), part[(idx + 1)..].Trim());
        }
    }
}