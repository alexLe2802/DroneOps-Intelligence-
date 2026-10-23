using DroneOps.Application.DTOs.Response.Notifications;

namespace DroneOps.Application.Interfaces.Notifications;

public interface INotificationService
{
    Task<NotificationFeedResponse> GetFeedAsync(Guid userId, bool unreadOnly, int limit, CancellationToken cancellationToken = default);
    Task<int> GetUnreadCountAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<NotificationResponse?> MarkAsReadAsync(Guid id, Guid userId, CancellationToken cancellationToken = default);
    Task<int> MarkAllAsReadAsync(Guid userId, CancellationToken cancellationToken = default);
    Task PublishPendingMissionAsync(Guid missionId, Guid actorUserId, string missionName, CancellationToken cancellationToken = default);
    Task PublishMissionDecisionAsync(Guid missionId, Guid actorUserId, bool approved, CancellationToken cancellationToken = default);
}
