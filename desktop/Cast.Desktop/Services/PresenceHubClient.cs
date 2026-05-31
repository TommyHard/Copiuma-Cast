using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace Cast.Desktop.Services;

/// <summary>
/// Платформенное присутствие десктопа. Держит постоянное соединение с
/// /hubs/presence, пока пользователь залогинен. Без него бэкенд не считает
/// стримера онлайн (нет строки UserConnection), и StatusService показывает
/// Offline вместо "Играет в ...". Команды слать не нужно — важен сам факт
/// живого соединения
/// </summary>
public sealed class PresenceHubClient : IAsyncDisposable
{
    private readonly DesktopOptions _options;
    private readonly Func<string> _tokenProvider;

    private HubConnection? _connection;

    public PresenceHubClient(DesktopOptions options, Func<string> tokenProvider)
    {
        _options = options;
        _tokenProvider = tokenProvider;
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        var url = _options.GatewayUrl.TrimEnd('/') + "/hubs/presence";
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

        await _connection.StartAsync(ct).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
            await _connection.DisposeAsync().ConfigureAwait(false);
    }
}
