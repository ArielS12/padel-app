using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Padel.Api.Contracts;
using Padel.Api.Data;

namespace Padel.Api.Controllers;

[Authorize]
public sealed class NotificationsController(AppDbContext db) : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyCollection<NotificationResponse>>> Get(CancellationToken cancellationToken)
    {
        var notifications = await db.Notifications
            .Where(notification => notification.UserId == CurrentUserId)
            .OrderByDescending(notification => notification.CreatedAtUtc)
            .Select(notification => new NotificationResponse(
                notification.Id,
                notification.Type,
                notification.Title,
                notification.Message,
                notification.IsRead,
                notification.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        return Ok(notifications);
    }

    [HttpPost("{notificationId:guid}/read")]
    public async Task<IActionResult> MarkAsRead(Guid notificationId, CancellationToken cancellationToken)
    {
        var notification = await db.Notifications.SingleOrDefaultAsync(x => x.Id == notificationId && x.UserId == CurrentUserId, cancellationToken);
        if (notification is null)
        {
            return NotFound();
        }

        notification.IsRead = true;
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }
}
