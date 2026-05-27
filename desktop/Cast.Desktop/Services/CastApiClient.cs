using Cast.Desktop.Models;
using System.Net.Http;
using System.Net.Http.Json;

namespace Cast.Desktop.Services;

/// <summary>
/// REST-клиент к бэкенду через шлюз. Комнаты, моды, инвайты
/// </summary>
public sealed class CastApiClient
{
    private readonly HttpClient _http;

    public CastApiClient(DesktopOptions options, string token)
    {
        _http = new HttpClient { BaseAddress = new Uri(options.GatewayUrl.TrimEnd('/') + "/api/") };
        _http.DefaultRequestHeaders.Authorization = new("Bearer", token);
    }

    // --- Rooms ---

    private sealed record CreateRoomRequest(string Title, string? GameId, int ViewerLimit);

    public sealed record RoomResponse(
        Guid Id, string Code, string Title, string? GameId,
        bool IsOpen, string Role, int ViewerLimit, int OnlineCount);

    /// <summary>
    /// Создать комнату; возвращает полную информацию
    /// </summary>
    public async Task<RoomResponse> CreateRoomAsync(string title, string? gameId,
        int viewerLimit = 0, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync("rooms",
            new CreateRoomRequest(title, gameId, viewerLimit), ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<RoomResponse>(cancellationToken: ct).ConfigureAwait(false)
               ?? throw new InvalidOperationException("Пустой ответ при создании комнаты.");
    }

    /// <summary>
    /// Закрыть комнату
    /// </summary>
    public async Task CloseRoomAsync(Guid roomId, CancellationToken ct = default)
    {
        var resp = await _http.PostAsync($"rooms/{roomId}/close", null, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Пригласить по @identifier
    /// </summary>
    public async Task InviteAsync(Guid roomId, string identifier, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync($"rooms/{roomId}/invite",
            new { Identifier = identifier }, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Получить ссылку-приглашение
    /// </summary>
    public async Task<string> GetInviteLinkAsync(Guid roomId, CancellationToken ct = default)
    {
        var resp = await _http.GetAsync($"rooms/{roomId}/invite-link", ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        var result = await resp.Content.ReadFromJsonAsync<InviteLinkResponse>(cancellationToken: ct)
                     .ConfigureAwait(false);
        return result?.Link ?? string.Empty;
    }

    private sealed record InviteLinkResponse(string Link);

    // --- Mods ---

    /// <summary>
    /// Список доступных модов/игр с сервера
    /// </summary>
    public async Task<List<ModPackageInfo>> GetAvailableModsAsync(CancellationToken ct = default)
    {
        return await _http.GetFromJsonAsync<List<ModPackageInfo>>("mods/packages", ct)
                   .ConfigureAwait(false) ?? new();
    }

    /// <summary>
    /// Получить пакет мода для конкретной игры
    /// </summary>
    public async Task<ModPackageInfo?> GetModPackageAsync(string gameId, CancellationToken ct = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<ModPackageInfo>($"mods/packages/{gameId}", ct)
                       .ConfigureAwait(false);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    // --- Games ---

    public sealed record GameInfo(string GameId, string GameName, string? Description);

    /// <summary>
    /// Список доступных игр на платформе
    /// </summary>
    public async Task<List<GameInfo>> GetGamesAsync(CancellationToken ct = default)
    {
        return await _http.GetFromJsonAsync<List<GameInfo>>("games", ct)
                   .ConfigureAwait(false) ?? new();
    }
}