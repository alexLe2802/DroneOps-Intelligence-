using System.Security.Claims;
using DroneOps.Application.DTOs.Response.Notifications;
using DroneOps.Application.Interfaces.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DroneOps.API.Controllers.Notifications;

[ApiController]
[Route("api/notifications")]
[Authorize]
public sealed class NotificationsController(INotificationService notificationService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(NotificationFeedResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetFeed(
        [FromQuery] bool unreadOnly = false,
        [FromQuery] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized(new { message = "Invalid authenticated user." });
        return Ok(await notificationService.GetFeedAsync(userId, unreadOnly, limit, cancellationToken));
    }

    [HttpGet("unread-count")]
    public async Task<IActionResult> GetUnreadCount(CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized(new { message = "Invalid authenticated user." });
        return Ok(new { unreadCount = await notificationService.GetUnreadCountAsync(userId, cancellationToken) });
    }

    [HttpPatch("{id:guid}/read")]
    [HttpPost("{id:guid}/seen")]
    [ProducesResponseType(typeof(NotificationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MarkAsRead(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized(new { message = "Invalid authenticated user." });
        var notification = await notificationService.MarkAsReadAsync(id, userId, cancellationToken);
        return notification is null
            ? NotFound(new { message = "Notification not found." })
            : Ok(notification);
    }

    [HttpPatch("read-all")]
    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllAsRead(CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized(new { message = "Invalid authenticated user." });
        var updatedCount = await notificationService.MarkAllAsReadAsync(userId, cancellationToken);
        return Ok(new { updatedCount });
    }

    private bool TryGetUserId(out Guid userId)
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(value, out userId);
    }
}
