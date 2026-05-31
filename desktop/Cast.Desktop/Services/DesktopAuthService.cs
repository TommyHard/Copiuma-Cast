using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
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
    /// Роли из JWT (claim ClaimTypes.Role). Нужны, чтобы пускать в десктоп только
    /// стримеров и админов
    /// </summary>
    public IReadOnlyList<string> GetRoles()
    {
        var t = Token;
        if (string.IsNullOrEmpty(t)) return Array.Empty<string>();
        var parts = t.Split('.');
        if (parts.Length < 2) return Array.Empty<string>();
        try
        {
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload += (payload.Length % 4) switch { 2 => "==", 3 => "=", _ => "" };
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var roles = new List<string>();
            foreach (var key in new[]
            {
                "http://schemas.microsoft.com/ws/2008/06/identity/claims/role", "role", "roles"
            })
            {
                if (!root.TryGetProperty(key, out var el)) continue;
                if (el.ValueKind == JsonValueKind.String) roles.Add(el.GetString()!);
                else if (el.ValueKind == JsonValueKind.Array)
                    roles.AddRange(el.EnumerateArray()
                        .Where(x => x.ValueKind == JsonValueKind.String)
                        .Select(x => x.GetString()!));
            }
            return roles;
        }
        catch { return Array.Empty<string>(); }
    }

    /// <summary>
    /// Разрешён ли вход в desktop (только стример/админ)
    /// </summary>
    public bool IsStreamerOrAdmin()
    {
        var roles = GetRoles();
        return roles.Contains("Streamer") || roles.Contains("Admin");
    }

    /// <summary>
    /// Id пользователя из токена (claim sub/nameidentifier) — для привязки настроек
    /// </summary>
    public string? GetUserId()
    {
        var t = Token;
        if (string.IsNullOrEmpty(t)) return null;
        var parts = t.Split('.');
        if (parts.Length < 2) return null;
        try
        {
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload += (payload.Length % 4) switch { 2 => "==", 3 => "=", _ => "" };
            using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
            var root = doc.RootElement;
            foreach (var key in new[] { "sub", "nameid", "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier" })
                if (root.TryGetProperty(key, out var el) && el.ValueKind == JsonValueKind.String)
                    return el.GetString();
            return null;
        }
        catch { return null; }
    }

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