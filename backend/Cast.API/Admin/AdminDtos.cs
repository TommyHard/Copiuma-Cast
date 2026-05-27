using Cast.API.Domain;

namespace Cast.API.Admin;

public sealed record StreamerApplicationDto(
    Guid Id,
    Guid UserId,
    string DisplayName,
    string Handle,
    string? Message,
    ApplicationStatus Status,
    DateTimeOffset CreatedAt);

public sealed record AdminUserDto(
    Guid Id,
    string Email,
    string DisplayName,
    string Handle,
    bool IsBlocked);