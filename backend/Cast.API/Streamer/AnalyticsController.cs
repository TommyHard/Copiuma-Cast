using Cast.API.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Cast.API.Streamer;

[ApiController]
[Route("api/streamer/analytics")]
[Authorize]
public sealed class AnalyticsController : ControllerBase
{
    private readonly AnalyticsService _analytics;

    public AnalyticsController(AnalyticsService analytics) => _analytics = analytics;

    /// <summary>
    /// Дашборд стримера: оборот, топ-зрители, популярные события и медиа
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<AnalyticsDto>> Dashboard(CancellationToken ct)
    {
        var me = User.GetUserId();
        if (me is null) return Unauthorized();
        return Ok(await _analytics.GetDashboardAsync(me.Value, ct));
    }
}