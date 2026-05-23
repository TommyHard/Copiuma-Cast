using System.Text;
using Cast.Shared.GameBridge;

namespace Cast.GameBridge;

/// <summary>
/// Превращает <see cref="GameCommand"/> в строку «провода», понятную моду.
/// Вынесено в абстракцию, чтобы под разные моды/игры можно было задать разный
/// формат, не трогая транспорты
/// </summary>
public interface ICommandFormatter
{
    string Format(GameCommand command);
}

/// <summary>
/// Формат текущего мода GTA SA: токены вида <c>key=value</c>,
/// разделённые <c>" &amp; "</c>, одна команда в строке. Обязательные токены —
/// <c>eventid</c> и <c>username</c>, далее произвольные параметры события.
/// Совпадает с парсером event.lua (split по '&', пары param=value, без пробелов
/// внутри значений)
/// </summary>
public sealed class KeyValueCommandFormatter : ICommandFormatter
{
    public string Format(GameCommand command)
    {
        var sb = new StringBuilder();
        sb.Append("eventid=").Append(Sanitize(command.EventId));
        sb.Append(" & username=").Append(Sanitize(command.Username));

        foreach (var (key, value) in command.Args)
            sb.Append(" & ").Append(Sanitize(key)).Append('=').Append(Sanitize(value));

        return sb.ToString();
    }

    /// <summary>
    /// Парсер мода читает значения как <c>[^%s]+</c> и режет по '&', поэтому
    /// внутри значений недопустимы пробелы и '&'. Заменяем их на '_' /
    /// удаляем, чтобы команда осталась корректной (особенно важно для ников)
    /// </summary>
    private static string Sanitize(string s)
    {
        if (string.IsNullOrEmpty(s))
            return string.Empty;
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            if (ch == '&' || ch == '=')
                continue;
            sb.Append(char.IsWhiteSpace(ch) ? '_' : ch);
        }
        return sb.ToString();
    }
}
