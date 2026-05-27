using System.Text;

namespace Cast.API.Common;

/// <summary>
/// Нормализация и проверка @identifier (хэндла). Разрешены латиница, цифры и
/// подчёркивание; хранится в нижнем регистре, длина 3..30
/// </summary>
public static class HandleUtil
{
    public const int MinLength = 3;
    public const int MaxLength = 30;

    /// <summary>
    /// Приводит произвольную строку к допустимому виду (нижний регистр,
    /// только [a-z0-9_], обрезка до MaxLength). Может вернуть пустую строку
    /// </summary>
    public static string Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        var sb = new StringBuilder(raw.Length);
        foreach (var ch in raw.Trim().ToLowerInvariant())
        {
            if (ch is >= 'a' and <= 'z' or >= '0' and <= '9' || ch == '_')
                sb.Append(ch);
            if (sb.Length >= MaxLength)
                break;
        }
        return sb.ToString();
    }

    public static bool IsValid(string handle)
        => handle.Length is >= MinLength and <= MaxLength;
}