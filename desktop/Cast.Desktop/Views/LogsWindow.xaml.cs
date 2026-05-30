using System.Collections.Specialized;
using System.Windows;
using Cast.Desktop.Services;

namespace Cast.Desktop.Views;

public partial class LogsWindow : Window
{
    public LogsWindow()
    {
        InitializeComponent();
        LogList.ItemsSource = AppLog.Entries;
        AppLog.Entries.CollectionChanged += OnEntriesChanged;
        Closed += (_, _) => AppLog.Entries.CollectionChanged -= OnEntriesChanged;
        ScrollToEnd();
    }

    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (AutoScroll.IsChecked == true)
            ScrollToEnd();
    }

    private void ScrollToEnd()
    {
        if (LogList.Items.Count > 0)
            LogList.ScrollIntoView(LogList.Items[LogList.Items.Count - 1]);
    }

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        try 
        { 
            Clipboard.SetText(AppLog.DumpText()); 
        }
        catch (Exception ex)
        {
            AppLog.Info($"Ошибка: {ex.Message}");
        }
    }

    private void OnCopyRow(object sender, RoutedEventArgs e)
    {
        if (LogList.SelectedItem is LogEntry entry)
        {
            try { Clipboard.SetText(entry.ToString()); } 
            catch (Exception ex)
            {
                AppLog.Info($"Ошибка: {ex.Message}");
            }
        }
    }

    private void OnClear(object sender, RoutedEventArgs e) => AppLog.Clear();
}