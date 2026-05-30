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

    public event Action<string>? Log;
    public event Action<RoomRoster>? RosterChanged;

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
    public async Task ConnectAsync(string code, CancellationToken ct = default)
    {
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

        _connection.Reconnected += async _ => { await _connection.InvokeAsync("JoinRoom", code); };

        await _connection.StartAsync(ct).ConfigureAwait(false);
        await _connection.InvokeAsync("JoinRoom", code, ct).ConfigureAwait(false);
        Log?.Invoke($"Подключено к комнате {code}.");
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
        if (_connection is not null)
            await _connection.DisposeAsync().ConfigureAwait(false);
    }
}