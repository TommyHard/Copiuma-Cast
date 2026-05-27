using System.Windows;

namespace Cast.Desktop.Localization;

/// <summary>
/// Переключение языка интерфейса через ResourceDictionary (en.xaml / ru.xaml)
/// При смене языка заменяет словарь в MergedDictionaries приложения
/// </summary>
public static class LocalizationManager
{
    private static readonly Uri EnUri = new("pack://application:,,,/Localization/en.xaml");
    private static readonly Uri RuUri = new("pack://application:,,,/Localization/ru.xaml");

    private static ResourceDictionary? _current;

    public static string CurrentLanguage { get; private set; } = "ru";

    public static void SetLanguage(string lang)
    {
        var uri = lang == "en" ? EnUri : RuUri;
        var dict = new ResourceDictionary { Source = uri };

        var merged = Application.Current.Resources.MergedDictionaries;
        if (_current is not null)
            merged.Remove(_current);
        merged.Add(dict);
        _current = dict;
        CurrentLanguage = lang;
    }

    /// <summary>
    /// Получить строку по ключу из текущего словаря локализации
    /// </summary>
    public static string Get(string key)
        => Application.Current.TryFindResource(key) as string ?? $"[{key}]";
}