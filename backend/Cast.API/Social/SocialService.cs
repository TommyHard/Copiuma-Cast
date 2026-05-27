using Cast.API.Data;
using Cast.API.Domain;
using Microsoft.EntityFrameworkCore;

namespace Cast.API.Social;

public enum FriendRequestResult { Sent, UserNotFound, CannotFriendSelf, AlreadyLinked }
public enum FollowResult { Followed, StreamerNotFound, CannotFollowSelf, AlreadyFollowing }

/// <summary>
/// Социальный граф: друзья (запрос/подтверждение/удаление) и подписки на
/// стримеров. Списки отдаются уже с вычисленным статусом присутствия
/// </summary>
public sealed class SocialService
{
    private readonly CastDbContext _db;
    private readonly StatusService _status;

    public SocialService(CastDbContext db, StatusService status)
    {
        _db = db;
        _status = status;
    }

    // ---- Друзья ----

    public async Task<List<UserCardDto>> FriendsAsync(Guid userId, CancellationToken ct = default)
    {
        var ids = await _db.FriendLinks
            .Where(f => f.Status == FriendStatus.Accepted && (f.RequesterId == userId || f.AddresseeId == userId))
            .Select(f => f.RequesterId == userId ? f.AddresseeId : f.RequesterId)
            .ToListAsync(ct);
        return await BuildCardsAsync(ids, ct);
    }

    public async Task<List<FriendRequestDto>> IncomingRequestsAsync(Guid userId, CancellationToken ct = default)
        => await (from f in _db.FriendLinks
                  where f.AddresseeId == userId && f.Status == FriendStatus.Pending
                  join u in _db.Users on f.RequesterId equals u.Id
                  orderby f.CreatedAt descending
                  select new FriendRequestDto(f.Id, u.Id, u.DisplayName, u.Handle, u.AvatarUrl, f.CreatedAt))
            .ToListAsync(ct);

    public async Task<FriendRequestResult> SendFriendRequestAsync(Guid userId, string handle, CancellationToken ct = default)
    {
        var normalized = handle.TrimStart('@').Trim().ToLowerInvariant();
        var target = await _db.Users.FirstOrDefaultAsync(u => u.Handle == normalized, ct);
        if (target is null)
            return FriendRequestResult.UserNotFound;
        if (target.Id == userId)
            return FriendRequestResult.CannotFriendSelf;

        var exists = await _db.FriendLinks.AnyAsync(f =>
            (f.RequesterId == userId && f.AddresseeId == target.Id) ||
            (f.RequesterId == target.Id && f.AddresseeId == userId), ct);
        if (exists)
            return FriendRequestResult.AlreadyLinked;

        _db.FriendLinks.Add(new FriendLink { RequesterId = userId, AddresseeId = target.Id });
        await _db.SaveChangesAsync(ct);
        return FriendRequestResult.Sent;
    }

    public async Task<bool> AcceptRequestAsync(Guid userId, Guid linkId, CancellationToken ct = default)
    {
        var link = await _db.FriendLinks.FirstOrDefaultAsync(f =>
            f.Id == linkId && f.AddresseeId == userId && f.Status == FriendStatus.Pending, ct);
        if (link is null)
            return false;
        link.Status = FriendStatus.Accepted;
        link.RespondedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// Удалить друга или отклонить/отозвать запрос — снимает связь в любом
    /// направлении и любом статусе
    /// </summary>
    public async Task<bool> RemoveFriendAsync(Guid userId, Guid otherId, CancellationToken ct = default)
    {
        var removed = await _db.FriendLinks
            .Where(f => (f.RequesterId == userId && f.AddresseeId == otherId) ||
                        (f.RequesterId == otherId && f.AddresseeId == userId))
            .ExecuteDeleteAsync(ct);
        return removed > 0;
    }

    // ---- Подписки ----

    public async Task<List<UserCardDto>> FollowingAsync(Guid userId, CancellationToken ct = default)
    {
        var ids = await _db.Follows.Where(f => f.FollowerId == userId).Select(f => f.StreamerId).ToListAsync(ct);
        return await BuildCardsAsync(ids, ct);
    }

    public async Task<FollowResult> FollowAsync(Guid userId, Guid streamerId, CancellationToken ct = default)
    {
        if (streamerId == userId)
            return FollowResult.CannotFollowSelf;
        if (!await _db.Users.AnyAsync(u => u.Id == streamerId, ct))
            return FollowResult.StreamerNotFound;
        if (await _db.Follows.AnyAsync(f => f.FollowerId == userId && f.StreamerId == streamerId, ct))
            return FollowResult.AlreadyFollowing;

        _db.Follows.Add(new Follow { FollowerId = userId, StreamerId = streamerId });
        await _db.SaveChangesAsync(ct);
        return FollowResult.Followed;
    }

    public async Task<bool> UnfollowAsync(Guid userId, Guid streamerId, CancellationToken ct = default)
    {
        var removed = await _db.Follows
            .Where(f => f.FollowerId == userId && f.StreamerId == streamerId)
            .ExecuteDeleteAsync(ct);
        return removed > 0;
    }

    // ---- Сборка карточек со статусом ----

    /// <summary>
    /// Карточка одного пользователя со статусом
    /// </summary>
    public async Task<UserCardDto?> CardAsync(Guid userId, CancellationToken ct = default)
        => (await BuildCardsAsync(new[] { userId }, ct)).FirstOrDefault();


    private async Task<List<UserCardDto>> BuildCardsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
            return new List<UserCardDto>();

        var users = await _db.Users
            .Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.DisplayName, u.Handle, u.AvatarUrl })
            .ToListAsync(ct);

        var statuses = await _status.ResolveAsync(ids, ct);

        return users.Select(u =>
        {
            var (status, activity, target) = statuses.GetValueOrDefault(u.Id, (UserStatus.Offline, ActivityKind.None, null));
            return new UserCardDto(u.Id, u.DisplayName, u.Handle, u.AvatarUrl, status, activity, target);
        }).ToList();
    }
}