using System.Security.Claims;

namespace Cast.API.Common;

public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// Идентификатор текущего пользователя из JWT (claim NameIdentifier/sub)
    /// </summary>
    public static Guid? GetUserId(this ClaimsPrincipal principal)
    {
        var raw = principal.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? principal.FindFirstValue("sub");
        return Guid.TryParse(raw, out var id) ? id : null;
    }
}
