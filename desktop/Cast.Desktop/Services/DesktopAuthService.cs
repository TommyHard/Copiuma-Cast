using System.Diagnostics;
using System.Net;
using System.Web;

namespace Cast.Desktop.Services;

/// <summary>
/// Авторизация через браузер с возвратом токена в приложение.
/// Поднимает локальный loopback-слушатель, открывает страницу логина в браузере
/// с параметром desktop-callback; веб-интерфейс после входа редиректит на
/// callback с токеном, который и забираем
/// </summary>
public sealed class DesktopAuthService
{
    private readonly DesktopOptions _options;

    public DesktopAuthService(DesktopOptions options) => _options = options;

    public string? Token { get; private set; }

    /// <summary>
    /// Открывает браузер и ждёт возврат токена (с таймаутом)
    /// </summary>
    public async Task<string> LoginViaBrowserAsync(CancellationToken ct = default)
    {
        var listener = new HttpListener();
        var port = GetFreePort();
        var prefix = $"http://127.0.0.1:{port}/";
        listener.Prefixes.Add(prefix);
        listener.Start();

        try
        {
            var callback = Uri.EscapeDataString(prefix);
            var loginUrl = $"{_options.WebUrl.TrimEnd('/')}/login?desktop={callback}";
            Process.Start(new ProcessStartInfo(loginUrl) { UseShellExecute = true });

            using var reg = ct.Register(() => { try { listener.Stop(); } catch { } });
            var ctx = await listener.GetContextAsync().ConfigureAwait(false);

            var token = HttpUtility.ParseQueryString(ctx.Request.Url!.Query).Get("token");

            var html = "<html><body style='font-family:sans-serif;background:#0d0d10;color:#9BDF1E'>" +
                       "<h2>Можно вернуться в приложение Copiuma.Cast</h2></body></html>";
            var buf = System.Text.Encoding.UTF8.GetBytes(html);
            ctx.Response.ContentType = "text/html; charset=utf-8";
            await ctx.Response.OutputStream.WriteAsync(buf, ct).ConfigureAwait(false);
            ctx.Response.Close();

            if (string.IsNullOrEmpty(token))
                throw new InvalidOperationException("Токен не получен от веб-интерфейса.");

            Token = token;
            return token;
        }
        finally
        {
            listener.Close();
        }
    }

    private static int GetFreePort()
    {
        var l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }
}