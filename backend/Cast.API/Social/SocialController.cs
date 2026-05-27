using Cast.API.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Cast.API.Social;

[ApiController]
[Route("api/social")]
[Authorize]
public sealed class SocialController : ControllerBase
{
    private readonly SocialService _social;

    public SocialController(SocialService social) => _social = social;

    // ---- Друзья ----

    [HttpGet("friends")]
    public async Task<ActionResult<List<UserCardDto>>> Friends(CancellationToken ct)
        => Ok(await _social.FriendsAsync(Me(), ct));

    [HttpGet("friends/requests")]
    public async Task<ActionResult<List<FriendRequestDto>>> Requests(CancellationToken ct)
        => Ok(await _social.IncomingRequestsAsync(Me(), ct));

    [HttpPost("friends/requests")]
    public async Task<IActionResult> SendRequest(SendFriendRequest req, CancellationToken ct)
    {
        var result = await _social.SendFriendRequestAsync(Me(), req.Handle ?? string.Empty, ct);
        return result switch
        {
            FriendRequestResult.Sent => Ok(),
            FriendRequestResult.UserNotFound => NotFound("Пользователь не найден."),
            FriendRequestResult.CannotFriendSelf => BadRequest("Нельзя добавить себя."),
            FriendRequestResult.AlreadyLinked => Conflict("Запрос уже существует или вы уже друзья."),
            _ => BadRequest()
        };
    }

    [HttpPost("friends/requests/{linkId:guid}/accept")]
    public async Task<IActionResult> Accept(Guid linkId, CancellationToken ct)
        => await _social.AcceptRequestAsync(Me(), linkId, ct) ? Ok() : NotFound("Запрос не найден.");

    [HttpDelete("friends/{userId:guid}")]
    public async Task<IActionResult> RemoveFriend(Guid userId, CancellationToken ct)
        => await _social.RemoveFriendAsync(Me(), userId, ct) ? NoContent() : NotFound();

    // ---- Подписки ----

    [HttpGet("following")]
    public async Task<ActionResult<List<UserCardDto>>> Following(CancellationToken ct)
        => Ok(await _social.FollowingAsync(Me(), ct));

    [HttpPost("follow/{streamerId:guid}")]
    public async Task<IActionResult> Follow(Guid streamerId, CancellationToken ct)
    {
        var result = await _social.FollowAsync(Me(), streamerId, ct);
        return result switch
        {
            FollowResult.Followed => Ok(),
            FollowResult.StreamerNotFound => NotFound("Стример не найден."),
            FollowResult.CannotFollowSelf => BadRequest("Нельзя подписаться на себя."),
            FollowResult.AlreadyFollowing => Conflict("Вы уже подписаны."),
            _ => BadRequest()
        };
    }

    [HttpDelete("follow/{streamerId:guid}")]
    public async Task<IActionResult> Unfollow(Guid streamerId, CancellationToken ct)
        => await _social.UnfollowAsync(Me(), streamerId, ct) ? NoContent() : NotFound();

    private Guid Me() => User.GetUserId() ?? throw new InvalidOperationException("Нет идентификатора пользователя.");
}