using System.Net.Sockets;
using System.Text;
using Cast.Shared.GameBridge;

namespace Cast.GameBridge.Transports;

/// <summary>
/// Основной транспорт: loopback-сокет к моду игры (GTA SA слушает 127.0.0.1:14888).
/// Команды шлются построчно (строка + перевод строки). Поддерживает ленивое
/// подключение и переподключение: если мод ещё не поднялся или связь оборвалась,
/// следующая отправка попытается переподключиться
/// </summary>
public sealed class SocketGameBridge : IGameBridge
{
    private readonly string _host;
    private readonly int _port;
    private readonly string _lineTerminator;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private UdpClient? _client;

    public SocketGameBridge(string host, int port, string lineTerminator = "\n")
    {
        _host = host;
        _port = port;
        _lineTerminator = lineTerminator;
    }

    public GameBridgeTransport Transport => GameBridgeTransport.Socket;
    public bool IsAvailable => _client != null;

    public Task StartAsync(CancellationToken ct = default)
    {
        EnsureClient();
        return Task.CompletedTask;
    }

    public async Task SendAsync(string commandLine, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            EnsureClient();
            var bytes = Encoding.UTF8.GetBytes(commandLine + _lineTerminator);

            await _client!.SendAsync(bytes, ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private void EnsureClient()
    {
        if (_client == null)
        {
            _client = new UdpClient();
            _client.Connect(_host, _port);
        }
    }

    private void Reset()
    {
        try { _client?.Dispose(); } catch { /* ignore */ }
        _client = null;
    }

    public ValueTask DisposeAsync()
    {
        Reset();
        _gate.Dispose();
        return ValueTask.CompletedTask;
    }
}