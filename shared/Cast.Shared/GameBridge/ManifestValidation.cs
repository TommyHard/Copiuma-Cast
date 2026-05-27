using System.Globalization;

namespace Cast.Shared.GameBridge;

/// <summary>
/// Валидация аргументов события против его определения в манифесте.
/// Единый источник правды для десктопа (GameBridge, защита у транспорта) и
/// бэкенда (авторитетная проверка перед списанием валюты и доставкой команды)
/// </summary>
public static class ManifestValidation
{
    /// <summary>
    /// Проверяет аргументы по описанию события. Не бросает исключений — при
    /// ошибке возвращает false и заполняет <paramref name="reason"/>
    /// </summary>
    public static bool ValidateParams(GameEventDefinition def,
        IReadOnlyDictionary<string, string> args, out string? reason)
    {
        reason = null;
        foreach (var p in def.Params)
        {
            var has = args.TryGetValue(p.Name, out var raw);
            if (!has || string.IsNullOrEmpty(raw))
            {
                if (p.Required && string.IsNullOrEmpty(p.Default))
                {
                    reason = $"Не задан обязательный параметр '{p.Name}'.";
                    return false;
                }
                continue;
            }

            switch (p.Type)
            {
                case GameEventParamType.Int:
                    if (!long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i))
                    { reason = $"Параметр '{p.Name}' должен быть целым."; return false; }
                    if (!InRange(i, p)) { reason = $"Параметр '{p.Name}' вне диапазона."; return false; }
                    break;
                case GameEventParamType.Float:
                    if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                    { reason = $"Параметр '{p.Name}' должен быть числом."; return false; }
                    if (!InRange(d, p)) { reason = $"Параметр '{p.Name}' вне диапазона."; return false; }
                    break;
                case GameEventParamType.Bool:
                    if (!bool.TryParse(raw, out _))
                    { reason = $"Параметр '{p.Name}' должен быть true/false."; return false; }
                    break;
                case GameEventParamType.Enum:
                    if (p.EnumValues is { Count: > 0 } &&
                        !p.EnumValues.Contains(raw, StringComparer.OrdinalIgnoreCase))
                    { reason = $"Параметр '{p.Name}' не из списка допустимых."; return false; }
                    break;
                case GameEventParamType.String:
                default:
                    break;
            }
        }
        return true;
    }

    private static bool InRange(double value, GameEventParam p)
        => (p.Min is null || value >= p.Min) && (p.Max is null || value <= p.Max);
}