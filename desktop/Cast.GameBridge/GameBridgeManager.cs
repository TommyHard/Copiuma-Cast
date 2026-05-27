using Cast.GameBridge.Transports;
using Cast.Shared.GameBridge;
using System.Globalization;

namespace Cast.GameBridge;

/// <summary>
/// Итог попытки выполнить команду через мост
/// </summary>
public enum DispatchStatus
{
    Ok,
    /// <summary>
    /// События нет в манифесте (белом списке) либо оно выключено
    /// </summary>
    UnknownOrDisabledEvent,
    /// <summary>
    /// Параметры не прошли валидацию по манифесту
    /// </summary>
    InvalidParams,
    /// <summary>
    /// Событие на кулдауне
    /// </summary>
    Cooldown,
    /// <summary>
    /// Транспорт недоступен / ошибка отправки
    /// </summary>
    TransportError
}

public readonly record struct DispatchResult(DispatchStatus Status, string? Reason = null)
{
    public bool Ok => Status == DispatchStatus.Ok;
    public static DispatchResult Success => new(DispatchStatus.Ok);
}

/// <summary>
/// Связывает манифест, валидацию и транспорт. Принимает <see cref="GameCommand"/>
/// (сформированную по запросу зрителя), проверяет её по манифесту-белому-списку,
/// применяет кулдаун, форматирует и отправляет моду выбранным транспортом.
///
/// Создаётся через <see cref="Create"/> по загруженному манифесту. Один экземпляр
/// на сессию игры
/// </summary>
public sealed class GameBridgeManager : IAsyncDisposable
{
    private readonly GameManifest _manifest;
    private readonly IGameBridge _transport;
    private readonly ICommandFormatter _formatter;
    private readonly Dictionary<string, GameEventDefinition> _events;
    private readonly Dictionary<string, long> _lastFiredTicks = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _cooldownLock = new();

    public GameManifest Manifest => _manifest;
    public IGameBridge Transport => _transport;

    public GameBridgeManager(GameManifest manifest, IGameBridge transport, ICommandFormatter? formatter = null)
    {
        _manifest = manifest;
        _transport = transport;
        _formatter = formatter ?? new KeyValueCommandFormatter();
        _events = manifest.Events.ToDictionary(e => e.Id, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Фабрика: выбирает транспорт по манифесту (Socket/File; Memory пока не
    /// поддержан) и собирает менеджер. <paramref name="modDirectory"/> нужен для
    /// файлового транспорта (там лежит файл-мост)
    /// </summary>
    public static GameBridgeManager Create(GameManifest manifest, string? modDirectory = null,
        ICommandFormatter? formatter = null)
    {
        IGameBridge transport = manifest.Transport switch
        {
            GameBridgeTransport.Socket => new SocketGameBridge(manifest.SocketHost, manifest.SocketPort),
            GameBridgeTransport.File => new FileGameBridge(
                Path.Combine(modDirectory ?? Directory.GetCurrentDirectory(), manifest.FileName)),
            _ => throw new NotSupportedException(
                $"Транспорт {manifest.Transport} пока не поддержан (память — на будущее).")
        };
        return new GameBridgeManager(manifest, transport, formatter);
    }

    public Task StartAsync(CancellationToken ct = default) => _transport.StartAsync(ct);

    /// <summary>
    /// Проверяет и отправляет команду моду. Не бросает исключения по ожидаемым
    /// причинам (неизвестное событие, валидация, кулдаун) — возвращает результат
    /// </summary>
    public async Task<DispatchResult> DispatchAsync(GameCommand command, CancellationToken ct = default)
    {
        if (!_events.TryGetValue(command.EventId, out var def) || !def.Enabled)
            return new DispatchResult(DispatchStatus.UnknownOrDisabledEvent,
                $"Событие '{command.EventId}' отсутствует в манифесте или выключено.");

        if (!ValidateParams(def, command, out var reason))
            return new DispatchResult(DispatchStatus.InvalidParams, reason);

        if (!CheckAndStampCooldown(def))
            return new DispatchResult(DispatchStatus.Cooldown,
                $"Событие '{command.EventId}' на кулдауне ({def.CooldownMs} мс).");

        try
        {
            var line = _formatter.Format(command);
            await _transport.SendAsync(line, ct).ConfigureAwait(false);
            return DispatchResult.Success;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new DispatchResult(DispatchStatus.TransportError, ex.Message);
        }
    }

    private static bool ValidateParams(GameEventDefinition def, GameCommand command, out string? reason)
    {
        reason = null;
        foreach (var p in def.Params)
        {
            var has = command.Args.TryGetValue(p.Name, out var raw);
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

    private bool CheckAndStampCooldown(GameEventDefinition def)
    {
        if (def.CooldownMs <= 0)
            return true;
        var now = Environment.TickCount64;
        lock (_cooldownLock)
        {
            if (_lastFiredTicks.TryGetValue(def.Id, out var last) && now - last < def.CooldownMs)
                return false;
            _lastFiredTicks[def.Id] = now;
            return true;
        }
    }

    public ValueTask DisposeAsync() => _transport.DisposeAsync();
}