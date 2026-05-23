using Cast.API.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

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

        var user = new ApplicationUser
        {
            UserName = req.Email,
            Email = req.Email,
            DisplayName = string.IsNullOrWhiteSpace(req.DisplayName) ? req.Email : req.DisplayName,
            Coins = 1000
        };

        var result = await _users.CreateAsync(user, req.Password);
        if (!result.Succeeded)
            return BadRequest(result.Errors.Select(e => e.Description));

        return Ok(BuildResponse(user));
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

        return Ok(BuildResponse(user));
    }

    private AuthResponse BuildResponse(ApplicationUser user)
    {
        var (token, expiresAt) = _tokens.Create(user);
        return new AuthResponse(token, expiresAt, user.Id, user.DisplayName, user.Coins);
    }
}