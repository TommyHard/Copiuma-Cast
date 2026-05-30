using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;

namespace Cast.Desktop.Services;

public sealed record LogEntry(DateTime Time, string Level, string Message)
{
    public override string ToString() => $"[{Time:HH:mm:ss.fff}] {Level,-5} {Message}";
}

/// <summary>
/// Централизованный буфер логов приложения. Собирает события из самого
/// приложения (AppLog.Info/Warn/Error), вывод Trace/Debug и необработанные
/// исключения. Открывается окном логов по Shift+~
/// </summary>
public static class AppLog
{
    private const int MaxEntries = 2000;
    private static readonly object Gate = new();

    /// <summary>
    /// Лента записей (для привязки в окне логов)
    /// </summary>
    public static ObservableCollection<LogEntry> Entries { get; } = new();

    public static void Info(string message) => Add("INFO", message);
    public static void Warn(string message) => Add("WARN", message);
    public static void Error(string message) => Add("ERROR", message);

    public static void Add(string level, string message)
    {
        var entry = new LogEntry(DateTime.Now, level, message);
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            Append(entry);
        else
            dispatcher.BeginInvoke(DispatcherPriority.Background, () => Append(entry));
    }

    private static void Append(LogEntry entry)
    {
        lock (Gate)
        {
            Entries.Add(entry);
            while (Entries.Count > MaxEntries)
                Entries.RemoveAt(0);
        }
    }

    public static string DumpText()
    {
        lock (Gate)
            return string.Join(Environment.NewLine, Entries.Select(e => e.ToString()));
    }

    public static void Clear()
    {
        lock (Gate)
            Entries.Clear();
    }

    private static bool _installed;

    /// <summary>
    /// Подключить глобальный перехват: Trace/Debug и необработанные исключения
    /// </summary>
    public static void Install()
    {
        if (_installed) return;
        _installed = true;

        Trace.Listeners.Add(new AppLogTraceListener());

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Error($"Необработанное исключение: {(e.ExceptionObject as Exception)?.Message ?? e.ExceptionObject}");

        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Error($"Необработанное исключение в задаче: {e.Exception.Message}");
            e.SetObserved();
        };

        if (Application.Current is { } app)
            app.DispatcherUnhandledException += (_, e) =>
                Error($"Ошибка UI: {e.Exception.Message}");
    }

    private sealed class AppLogTraceListener : TraceListener
    {
        public override void Write(string? message) { if (!string.IsNullOrEmpty(message)) Add("TRACE", message); }
        public override void WriteLine(string? message) { if (!string.IsNullOrEmpty(message)) Add("TRACE", message); }
    }
}
