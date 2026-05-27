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
    /// Уникальный @identifier (хэндл) для упоминаний и приглашений. Хранится в
    /// нижнем регистре; уникальность — на индексе
    /// </summary>
    public string Handle { get; set; } = string.Empty;

    /// <summary>
    /// URL аватара (загрузка файла в объектное хранилище — отдельный пайплайн)
    /// </summary>
    public string? AvatarUrl { get; set; }

    /// <summary>
    /// Предпочитаемый язык интерфейса (ru/en), по умолчанию en
    /// </summary>
    public string Language { get; set; } = "en";

    /// <summary>
    /// Ручной статус присутствия (Online/Away/DoNotDisturb/Offline). Активность
    /// в комнате вычисляется отдельно и переопределяет его при отображении
    /// </summary>
    public UserStatus ManualStatus { get; set; } = UserStatus.Offline;

    /// <summary>
    /// LEGACY: глобальный баланс. Экономика комнат локализована по
    /// стримеру; это поле используется только как
    /// стартовое приветственное значение в Auth
    /// </summary>
    public long Coins { get; set; }

    /// <summary>
    /// Заблокирован администратором — вход запрещён
    /// </summary>
    public bool IsBlocked { get; set; }

    /// <summary>
    /// Дата регистрации в сервисе
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
