using System.Globalization;
using System.Windows.Data;

namespace Cast.Desktop.Converters;

/// <summary>
/// Сравнивает строку-значение с ConverterParameter для RadioButton.IsChecked.
/// Двусторонний: при IsChecked=true записывает ConverterParameter обратно
/// </summary>
public sealed class NavCheckConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is string s && parameter is string p &&
           string.Equals(s, p, StringComparison.OrdinalIgnoreCase);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true && parameter is string p ? p : Binding.DoNothing;
}