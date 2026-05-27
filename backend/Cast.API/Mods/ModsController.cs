using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;

namespace Cast.API.Mods;

/// <summary>
/// Эндпоинты для десктопного Mod Manager:
///   GET /api/mods/packages          — список всех доступных пакетов
///   GET /api/mods/packages/{gameId} — конкретный пакет
///   GET /api/mods/files/{gameId}/{**path} — скачать файл мода
/// </summary>
[ApiController]
[Route("api/mods")]
[Authorize]
public sealed class ModsController : ControllerBase
{
    private readonly ModPackageCatalog _catalog;

    public ModsController(ModPackageCatalog catalog) => _catalog = catalog;

    /// <summary>
    /// Все доступные пакеты модов
    /// </summary>
    [HttpGet("packages")]
    public ActionResult<List<ModPackageDto>> List()
        => Ok(_catalog.GetAll());

    /// <summary>
    /// Конкретный пакет мода по gameId
    /// </summary>
    [HttpGet("packages/{gameId}")]
    public ActionResult<ModPackageDto> Get(string gameId)
    {
        var pkg = _catalog.Get(gameId);
        return pkg is null ? NotFound() : Ok(pkg);
    }

    /// <summary>
    /// Скачать файл мода (вызывается десктопом при установке)
    /// </summary>
    [HttpGet("files/{gameId}/{**path}")]
    public IActionResult DownloadFile(string gameId, string path)
    {
        var filePath = _catalog.ResolveFilePath(gameId, path);
        if (filePath is null)
            return NotFound("Файл не найден.");

        var provider = new FileExtensionContentTypeProvider();
        if (!provider.TryGetContentType(filePath, out var contentType))
            contentType = "application/octet-stream";

        return PhysicalFile(filePath, contentType, Path.GetFileName(filePath));
    }
}