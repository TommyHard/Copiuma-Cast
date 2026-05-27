using Cast.API.Common;
using Cast.API.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Cast.API.Auth;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly JwtTokenService _tokens;

    public AuthController(UserManager<ApplicationUser> users, JwtTokenService tokens)
    {
        _users = users;
        _tokens = tokens;
    }

    /// <summary>
    /// Регистрация нового пользователя
    /// </summary>
    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password))
            return BadRequest("Email и пароль обязательны.");

        var handle = HandleUtil.Normalize(req.Handle);
        if (!HandleUtil.IsValid(handle))
            return BadRequest($"@identifier должен быть {HandleUtil.MinLength}–{HandleUtil.MaxLength} символов: a-z, 0-9, _.");
        if (await _users.Users.AnyAsync(u => u.Handle == handle))
            return Conflict("Этот @identifier уже занят.");

        var user = new ApplicationUser
        {
            UserName = req.Email,
            Email = req.Email,
            DisplayName = string.IsNullOrWhiteSpace(req.DisplayName) ? req.Email : req.DisplayName,
            Handle = handle,
            Language = "en",
            Coins = 1000,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var result = await _users.CreateAsync(user, req.Password);
        if (!result.Succeeded)
            return BadRequest(result.Errors.Select(e => e.Description));

        return Ok(await BuildResponseAsync(user));
    }

    /// <summary>
    /// Вход по email/паролю — выдаёт JWT
    /// </summary>
    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest req)
    {
        var user = await _users.FindByEmailAsync(req.Email);
        if (user is null || !await _users.CheckPasswordAsync(user, req.Password))
            return Unauthorized("Неверный email или пароль.");

        return Ok(await BuildResponseAsync(user));
    }

    private async Task<AuthResponse> BuildResponseAsync(ApplicationUser user)
    {
        var roles = await _users.GetRolesAsync(user);
        var (token, expiresAt) = _tokens.Create(user, roles);
        return new AuthResponse(token, expiresAt, user.Id, user.DisplayName,
            user.Handle, user.AvatarUrl, user.Language, user.Coins);
    }
}