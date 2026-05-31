using Cast.Desktop.Models;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

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
    /// Выдать монеты зрителю в кошельке стримера (валюта локальна для стримера)
    /// </summary>
    public async Task GrantCoinsAsync(Guid roomId, Guid targetUserId, long amount, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync($"rooms/{roomId}/grant",
            new { TargetUserId = targetUserId, Amount = amount }, ct).ConfigureAwait(false);
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

    // --- Room events (стример включает/выключает события на сессию) ---

    public sealed record RoomEvent(
        string EventId, string Title, string? Category,
        int CostCoins, int CooldownMs, bool Enabled);

    /// <summary>
    /// События игры комнаты с учётом включения/выключения стримером
    /// </summary>
    public async Task<List<RoomEvent>> GetRoomEventsAsync(Guid roomId, CancellationToken ct = default)
        => await _http.GetFromJsonAsync<List<RoomEvent>>($"rooms/{roomId}/events", ct)
               .ConfigureAwait(false) ?? new();

    /// <summary>
    /// Включить/выключить событие в комнате (только владелец)
    /// </summary>
    public async Task SetEventEnabledAsync(Guid roomId, string eventId, bool enabled, CancellationToken ct = default)
    {
        var resp = await _http.PutAsJsonAsync($"rooms/{roomId}/events/{eventId}",
            new { Enabled = enabled }, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
    }

    // --- Bets (ставки; стример создаёт/разрешает/отменяет) ---

    public sealed record BetOutcome(Guid Id, string Label, long Pool, decimal? Odds);

    public sealed record Bet(
        Guid Id, Guid RoomId, Guid StreamerId, string Title, string Status,
        DateTimeOffset LocksAt, DateTimeOffset? ResolvedAt, Guid? WinningOutcomeId, long TotalPool,
        List<BetOutcome> Outcomes,
        string? TopWinnerName, long TopWinnerPayout, long PaidOut,
        long? MyStake, long? MyPayout, string? MyStatus);

    private sealed record CreateBetRequest(string Title, List<string> Outcomes, int LocksInSeconds);
    private sealed record ResolveBetRequest(Guid WinningOutcomeId);

    /// <summary>
    /// Список ставок комнаты с пулами и коэффициентами
    /// </summary>
    public async Task<List<Bet>> GetBetsAsync(Guid roomId, CancellationToken ct = default)
        => await _http.GetFromJsonAsync<List<Bet>>($"rooms/{roomId}/bets", ct)
               .ConfigureAwait(false) ?? new();

    /// <summary>
    /// Создать ставку (минимум два исхода)
    /// </summary>
    public async Task<Bet?> CreateBetAsync(Guid roomId, string title, List<string> outcomes,
        int locksInSeconds, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync($"rooms/{roomId}/bets",
            new CreateBetRequest(title, outcomes, locksInSeconds), ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<Bet>(cancellationToken: ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Разрешить ставку, указав выигравший исход
    /// </summary>
    public async Task ResolveBetAsync(Guid roomId, Guid betId, Guid winningOutcomeId, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync($"rooms/{roomId}/bets/{betId}/resolve",
            new ResolveBetRequest(winningOutcomeId), ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Отменить ставку с возвратом средств
    /// </summary>
    public async Task CancelBetAsync(Guid roomId, Guid betId, CancellationToken ct = default)
    {
        var resp = await _http.PostAsync($"rooms/{roomId}/bets/{betId}/cancel", null, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
    }

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
            return await _http.GetFromJsonAsync<ModPackageInfo>($"mods/packages/{gameId}", ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    // --- Games ---

    // Бэкенд (GET /api/games) отдаёт карточки GameCardDto { slug, title, ... }.
    // Мапим slug -> GameId, title -> GameName, иначе поля не заполняются и
    // комната создаётся без GameId (нет событий у зрителей, нет статуса "играет")
    public sealed record GameInfo(
        [property: JsonPropertyName("slug")] string GameId,
        [property: JsonPropertyName("title")] string GameName,
        [property: JsonPropertyName("description")] string? Description);

    /// <summary>
    /// Список доступных игр на платформе
    /// </summary>
    public async Task<List<GameInfo>> GetGamesAsync(CancellationToken ct = default)
    {
        return await _http.GetFromJsonAsync<List<GameInfo>>("games", ct)
                   .ConfigureAwait(false) ?? new();
    }
}