using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Padel.Api.Contracts;
using Padel.Api.Data;
using Padel.Api.Domain;

namespace Padel.Api.Controllers;

[Authorize]
public sealed class ProfileController(UserManager<ApplicationUser> userManager, AppDbContext db) : ApiControllerBase
{
    [HttpGet("{userId?}")]
    public async Task<ActionResult<ProfileResponse>> Get(string? userId, CancellationToken cancellationToken)
    {
        var id = string.IsNullOrWhiteSpace(userId) ? CurrentUserId : userId;
        var user = await userManager.FindByIdAsync(id);
        if (user is null)
        {
            return NotFound();
        }

        var followers = await db.UserFollows.CountAsync(x => x.FollowedId == user.Id, cancellationToken);
        var following = await db.UserFollows.CountAsync(x => x.FollowerId == user.Id, cancellationToken);

        return Ok(new ProfileResponse(
            user.Id,
            user.Email ?? string.Empty,
            user.FullName,
            user.City,
            user.Phone,
            user.Bio,
            user.ProfilePhotoUrl,
            user.Category,
            user.Level,
            followers,
            following));
    }

    [HttpPut]
    public async Task<IActionResult> Update(UpdateProfileRequest request)
    {
        var user = await userManager.FindByIdAsync(CurrentUserId);
        if (user is null)
        {
            return NotFound();
        }

        user.FullName = request.FullName;
        user.City = request.City;
        user.Phone = request.Phone;
        user.Bio = request.Bio;
        user.ProfilePhotoUrl = request.ProfilePhotoUrl;
        user.Category = request.Category;
        user.Level = request.Level;

        var result = await userManager.UpdateAsync(user);
        return result.Succeeded ? NoContent() : ValidationProblem(string.Join("; ", result.Errors.Select(error => error.Description)));
    }

    [HttpPost("{userId}/follow")]
    public async Task<IActionResult> Follow(string userId, CancellationToken cancellationToken)
    {
        if (userId == CurrentUserId)
        {
            return BadRequest("No puedes seguirte a ti mismo.");
        }

        if (await db.UserFollows.AnyAsync(x => x.FollowerId == CurrentUserId && x.FollowedId == userId, cancellationToken))
        {
            return NoContent();
        }

        if (!await db.Users.AnyAsync(x => x.Id == userId, cancellationToken))
        {
            return NotFound();
        }

        db.UserFollows.Add(new UserFollow { FollowerId = CurrentUserId, FollowedId = userId });
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpDelete("{userId}/follow")]
    public async Task<IActionResult> Unfollow(string userId, CancellationToken cancellationToken)
    {
        var follow = await db.UserFollows.SingleOrDefaultAsync(x => x.FollowerId == CurrentUserId && x.FollowedId == userId, cancellationToken);
        if (follow is not null)
        {
            db.UserFollows.Remove(follow);
            await db.SaveChangesAsync(cancellationToken);
        }

        return NoContent();
    }
}
