using Microsoft.AspNetCore.Identity;

namespace Cast.API.Domain;

/// <summary>
/// Пользователь системы (стример или зритель). Наследует ASP.NET Core Identity
/// с Guid-ключом. Виртуальная валюта хранится прямо здесь для простоты первого
/// среза;
/// </summary>
public sealed class ApplicationUser : IdentityUser<Guid>
{
    /// <summary>
    /// Отображаемое имя (ник в комнате)
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Баланс виртуальной валюты
    /// </summary>
    public long Coins { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
