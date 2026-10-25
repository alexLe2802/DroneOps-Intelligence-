namespace DroneOps.Application.DTOs.Response.Notifications;

public sealed class NotificationFeedResponse
{
    public int UnreadCount { get; set; }
    public List<NotificationResponse> Items { get; set; } = [];
}
