namespace Cast.API.Auth;

/// <summary>
/// Настройки JWT
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "Copiuma.Cast";
    public string Audience { get; set; } = "Copiuma.Cast.Clients";
    public string SigningKey { get; set; } = string.Empty;
    public int AccessTokenMinutes { get; set; } = 120;
}
