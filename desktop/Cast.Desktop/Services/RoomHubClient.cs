using Cast.Shared.GameBridge;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace Cast.Desktop.Services;

/// <summary>
/// SignalR-клиент игрового хаба. Стример-десктоп подключается к /hubs/room,
/// входит в комнату (попадает в группу стримера) и принимает готовые
/// <see cref="GameCommand"/>, которые тут же отдаёт моду через GameBridge.
/// Протокол и транспорт совпадают с бэкендом: WebSockets без negotiate +
/// MessagePack
/// </summary>
public sealed class RoomHubClient : IAsyncDisposable
{
    private readonly DesktopOptions _options;
    private readonly Func<string> _tokenProvider;
    private readonly GameBridgeService _bridge;

    private HubConnection? _connection;
    private Guid _roomId;
    private Timer? _bridgeHeartbeat;

    public event Action<string>? Log;
    public event Action<RoomRoster>? RosterChanged;
    /// <summary>
    /// Сервер сообщил об изменении ставки — UI должен перечитать список
    /// </summary>
    public event Action? BetUpdated;
    /// <summary>
    /// Сервер прислал новую громкость медиа (синхронизация с оверлеем)
    /// </summary>
    public event Action<int>? MediaVolumeChanged;

    public RoomHubClient(DesktopOptions options, Func<string> tokenProvider, GameBridgeService bridge)
    {
        _options = options;
        _tokenProvider = tokenProvider;
        _bridge = bridge;
    }

    public HubConnectionState State => _connection?.State ?? HubConnectionState.Disconnected;

    /// <summary>
    /// Подключиться к хабу и войти в комнату по коду. После входа начинают
    /// приходить команды, которые форвардятся моду
    /// </summary>
    public async Task ConnectAsync(Guid roomId, string code, CancellationToken ct = default)
    {
        _roomId = roomId;
        var url = _options.GatewayUrl.TrimEnd('/') + "/hubs/room";
        _connection = new HubConnectionBuilder()
            .WithUrl(url, o =>
            {
                o.AccessTokenProvider = () => Task.FromResult(_tokenProvider())!;
                o.SkipNegotiation = true;
                o.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.WebSockets;
            })
            .AddMessagePackProtocol()
            .WithAutomaticReconnect()
            .Build();

        // Приём команды зрителя -> доставка моду через GameBridge
        _connection.On<GameCommand>("GameCommand", async cmd =>
        {
            var result = await _bridge.DispatchAsync(cmd).ConfigureAwait(false);
            Log?.Invoke(result.Ok
                ? $"→ моду: {cmd.EventId} (от {cmd.Username})"
                : $"отклонено [{result.Status}]: {result.Reason}");
        });

        // Обновление списка подключённых участников комнаты
        _connection.On<RoomRoster>("RoomRoster", roster => RosterChanged?.Invoke(roster));

        // Изменение ставки (создана/ставка сделана/разрешена/отменена).
        // Без аргумента: сервер шлёт пустой сигнал, UI перечитывает список по REST
        _connection.On("BetUpdated", () => BetUpdated?.Invoke());

        // Синхронизация громкости медиа (десктоп <> оверлей)
        _connection.On<int>("MediaVolumeChanged", v => MediaVolumeChanged?.Invoke(v));

        _connection.Reconnected += async _ => { await _connection.InvokeAsync("JoinRoom", code); };

        await _connection.StartAsync(ct).ConfigureAwait(false);
        await _connection.InvokeAsync("JoinRoom", code, ct).ConfigureAwait(false);
        Log?.Invoke($"Подключено к комнате {code}.");

        // Heartbeat готовности моста: сервер не списывает баллы, пока мост с игрой
        // не подключён. Шлём текущее состояние сразу и далее раз в 5 секунд
        await ReportBridgeAsync().ConfigureAwait(false);
        _bridgeHeartbeat = new Timer(async _ => await ReportBridgeAsync().ConfigureAwait(false),
            null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// Задать громкость медиа (0-100) — синхронизируется с оверлеем
    /// </summary>
    public async Task SetMediaVolumeAsync(int volume)
    {
        if (_connection is { State: HubConnectionState.Connected })
            try { await _connection.InvokeAsync("SetMediaVolume", _roomId, volume).ConfigureAwait(false); }
            catch { /* ignore */ }
    }

    private async Task ReportBridgeAsync()
    {
        if (_connection is not { State: HubConnectionState.Connected }) return;
        try { await _connection.InvokeAsync("ReportBridgeReady", _roomId, _bridge.IsTransportAvailable).ConfigureAwait(false); }
        catch { /* heartbeat ignore */ }
    }

    /// <summary>
    /// Выгнать (ban=false) или заблокировать (ban=true) зрителя
    /// </summary>
    public async Task KickAsync(Guid roomId, Guid targetUserId, bool ban)
    {
        if (_connection is not null)
            await _connection.InvokeAsync("KickViewer", roomId, targetUserId, ban).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_bridgeHeartbeat is not null)
            await _bridgeHeartbeat.DisposeAsync().ConfigureAwait(false);
        if (_connection is not null)
            await _connection.DisposeAsync().ConfigureAwait(false);
    }
}