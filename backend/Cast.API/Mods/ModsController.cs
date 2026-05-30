using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Cast.API.Mods;

/// <summary>
/// Эндпоинты для десктопного Mod Manager. Источник — БД игр (загруженные
/// админом мод-архив + манифест установки):
///   GET /api/mods/packages          — список всех доступных пакетов
///   GET /api/mods/packages/{gameId} — конкретный пакет
/// Файлы мода десктоп скачивает напрямую по ModPackageDto.ArchiveUrl (.zip)
/// </summary>
[ApiController]
[Route("api/mods")]
[Authorize]
public sealed class ModsController : ControllerBase
{
    private readonly ModService _mods;

    public ModsController(ModService mods) => _mods = mods;

    /// <summary>
    /// Все доступные пакеты модов
    /// </summary>
    [HttpGet("packages")]
    public async Task<ActionResult<List<ModPackageDto>>> List(CancellationToken ct)
        => Ok(await _mods.GetAllAsync(ct));

    /// <summary>
    /// Конкретный пакет мода по gameId
    /// </summary>
    [HttpGet("packages/{gameId}")]
    public async Task<ActionResult<ModPackageDto>> Get(string gameId, CancellationToken ct)
    {
        var pkg = await _mods.GetAsync(gameId, ct);
        return pkg is null ? NotFound() : Ok(pkg);
    }
}
