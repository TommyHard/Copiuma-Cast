using Cast.API.Domain;

namespace Cast.API.Social;

/// <summary>
/// Текущая активность пользователя, вычисляемая из присутствия в комнатах
/// </summary>
public enum ActivityKind
{
    None = 0,
    /// <summary>
    /// Смотрит стрим (target — имя стримера)
    /// </summary>
    Watching = 1,
    /// <summary>
    /// Играет/ведёт стрим (target — название игры)
    /// </summary>
    Playing = 2
}

/// <summary>
/// Карточка пользователя со статусом — для списков друзей и подписок
/// </summary>
public sealed record UserCardDto(
    Guid UserId,
    string DisplayName,
    string Handle,
    string? AvatarUrl,
    UserStatus Status,
    ActivityKind Activity,
    string? ActivityTarget);

/// <summary>
/// Входящий запрос в друзья
/// </summary>
public sealed record FriendRequestDto(
    Guid LinkId,
    Guid UserId,
    string DisplayName,
    string Handle,
    string? AvatarUrl,
    DateTimeOffset CreatedAt);

public sealed record SendFriendRequest(string Handle);