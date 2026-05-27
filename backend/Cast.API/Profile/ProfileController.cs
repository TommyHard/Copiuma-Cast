using Cast.API.Common;
using Cast.API.Domain;
using Cast.API.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Cast.API.Profile;

[ApiController]
[Route("api/profile")]
[Authorize]
public sealed class ProfileController : ControllerBase
{
    private static readonly string[] SupportedLanguages = { "en", "ru" };

    private static readonly Dictionary<string, string> AllowedImageTypes = new()
    {
        ["image/png"] = ".png",
        ["image/jpeg"] = ".jpg",
        ["image/webp"] = ".webp",
        ["image/gif"] = ".gif"
    };
    private const long MaxAvatarBytes = 5 * 1024 * 1024;

    private readonly UserManager<ApplicationUser> _users;
    private readonly StorageService _storage;

    public ProfileController(UserManager<ApplicationUser> users, StorageService storage)
    {
        _users = users;
        _storage = storage;
    }

    /// <summary>
    /// Профиль текущего пользователя
    /// </summary>
    [HttpGet("me")]
    public async Task<ActionResult<ProfileDto>> Me()
    {
        var user = await CurrentAsync();
        return user is null ? Unauthorized() : Ok(Map(user));
    }

    /// <summary>
    /// Частичное обновление профиля
    /// </summary>
    [HttpPut]
    public async Task<ActionResult<ProfileDto>> Update(UpdateProfileRequest req)
    {
        var user = await CurrentAsync();
        if (user is null) return Unauthorized();

        if (req.DisplayName is { } dn && !string.IsNullOrWhiteSpace(dn))
            user.DisplayName = dn.Trim();

        if (req.Handle is { } rawHandle)
        {
            var handle = HandleUtil.Normalize(rawHandle);
            if (!HandleUtil.IsValid(handle))
                return BadRequest($"Handle должен быть {HandleUtil.MinLength}–{HandleUtil.MaxLength} символов: a-z, 0-9, _.");
            if (!string.Equals(handle, user.Handle, StringComparison.Ordinal))
            {
                var taken = await _users.Users.AnyAsync(u => u.Handle == handle && u.Id != user.Id);
                if (taken) return Conflict("Этот @identifier уже занят.");
                user.Handle = handle;
            }
        }

        if (req.AvatarUrl is { } avatar)
            user.AvatarUrl = string.IsNullOrWhiteSpace(avatar) ? null : avatar.Trim();

        if (req.Language is { } lang && SupportedLanguages.Contains(lang.ToLowerInvariant()))
            user.Language = lang.ToLowerInvariant();

        var result = await _users.UpdateAsync(user);
        return result.Succeeded ? Ok(Map(user)) : BadRequest(result.Errors.Select(e => e.Description));
    }

    /// <summary>
    /// Загрузить аватар (изображение до 5 МБ) в объектное хранилище
    /// </summary>
    [HttpPost("avatar")]
    public async Task<ActionResult<ProfileDto>> UploadAvatar(IFormFile file)
    {
        var user = await CurrentAsync();
        if (user is null) return Unauthorized();

        if (file is null || file.Length == 0)
            return BadRequest("Файл не передан.");
        if (file.Length > MaxAvatarBytes)
            return BadRequest("Файл больше 5 МБ.");
        if (!AllowedImageTypes.TryGetValue(file.ContentType, out var ext))
            return BadRequest("Поддерживаются PNG, JPEG, WEBP, GIF.");

        var key = $"avatars/{user.Id}/{Guid.NewGuid():N}{ext}";
        await using var stream = file.OpenReadStream();
        var url = await _storage.UploadPublicAsync(stream, key, file.ContentType);

        user.AvatarUrl = url;
        var result = await _users.UpdateAsync(user);
        return result.Succeeded ? Ok(Map(user)) : BadRequest(result.Errors.Select(e => e.Description));
    }

    /// <summary>
    /// Смена пароля
    /// </summary>
    [HttpPost("password")]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest req)
    {
        var user = await CurrentAsync();
        if (user is null) return Unauthorized();

        var result = await _users.ChangePasswordAsync(user, req.CurrentPassword, req.NewPassword);
        return result.Succeeded ? NoContent() : BadRequest(result.Errors.Select(e => e.Description));
    }

    /// <summary>
    /// Выставить ручной статус присутствия (Online/Away/DoNotDisturb/Offline)
    /// </summary>
    [HttpPut("status")]
    public async Task<ActionResult<ProfileDto>> SetStatus(SetStatusRequest req)
    {
        var user = await CurrentAsync();
        if (user is null) return Unauthorized();

        user.ManualStatus = req.Status;
        var result = await _users.UpdateAsync(user);
        return result.Succeeded ? Ok(Map(user)) : BadRequest(result.Errors.Select(e => e.Description));
    }

    private Task<ApplicationUser?> CurrentAsync()
    {
        var id = User.GetUserId();
        return id is null ? Task.FromResult<ApplicationUser?>(null) : _users.FindByIdAsync(id.Value.ToString());
    }

    private static ProfileDto Map(ApplicationUser u)
        => new(u.Id, u.Email ?? string.Empty, u.DisplayName, u.Handle, u.AvatarUrl, u.Language, u.ManualStatus);
}