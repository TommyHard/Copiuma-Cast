using Cast.API.Domain;

namespace Cast.API.Profile;

public sealed record ProfileDto(
    Guid Id,
    string Email,
    string DisplayName,
    string Handle,
    string? AvatarUrl,
    string Language,
    UserStatus Status);

/// <summary>
/// Частичное обновление профиля: null-поля не меняются
/// </summary>
public sealed record UpdateProfileRequest(
    string? DisplayName,
    string? Handle,
    string? AvatarUrl,
    string? Language);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public sealed record SetStatusRequest(UserStatus Status);