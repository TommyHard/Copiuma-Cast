namespace Cast.API.Auth;

public sealed record RegisterRequest(string Email, string Password, string DisplayName, string Handle);

public sealed record LoginRequest(string Email, string Password);

public sealed record AuthResponse(
    string AccessToken,
    DateTimeOffset ExpiresAt,
    Guid UserId,
    string DisplayName,
    string Handle,
    string? AvatarUrl,
    string Language,
    long Coins);
