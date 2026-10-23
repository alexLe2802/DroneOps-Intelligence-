using DroneOps.Application.DTOs.Response.Notifications;
using DroneOps.Application.Interfaces.Missions;
using DroneOps.Application.Interfaces.Notifications;
using DroneOps.Domain.Entities;
using DroneOps.Persistence.Interfaces.Notifications;

namespace DroneOps.Application.Services.Notifications;

public sealed class NotificationService(
    INotificationRepository repository,
    IMissionRepository missionRepository) : INotificationService
{
    public async Task<NotificationFeedResponse> GetFeedAsync(Guid userId, bool unreadOnly, int limit, CancellationToken cancellationToken = default)
    {
        limit = Math.Clamp(limit, 1, 100);
        var items = await repository.GetForUserAsync(userId, unreadOnly, limit, cancellationToken);
        return new NotificationFeedResponse
        {
            UnreadCount = await repository.GetUnreadCountAsync(userId, cancellationToken),
            Items = items.Select(Map).ToList()
        };
    }

    public Task<int> GetUnreadCountAsync(Guid userId, CancellationToken cancellationToken = default) =>
        repository.GetUnreadCountAsync(userId, cancellationToken);

    public async Task<NotificationResponse?> MarkAsReadAsync(Guid id, Guid userId, CancellationToken cancellationToken = default)
    {
        var notification = await repository.GetOwnedAsync(id, userId, cancellationToken);
        if (notification is null) return null;
        if (!notification.IsRead)
        {
            notification.IsRead = true;
            notification.ReadAt = DateTimeOffset.UtcNow;
            await repository.SaveChangesAsync(cancellationToken);
        }
        return Map(notification);
    }

    public Task<int> MarkAllAsReadAsync(Guid userId, CancellationToken cancellationToken = default) =>
        repository.MarkAllAsReadAsync(userId, DateTimeOffset.UtcNow, cancellationToken);

    public async Task PublishPendingMissionAsync(Guid missionId, Guid actorUserId, string missionName, CancellationToken cancellationToken = default)
    {
        var recipients = await repository.GetActiveOperationManagerIdsAsync(cancellationToken);
        var notifications = recipients.Select(recipient => new Notification
        {
            Id = Guid.NewGuid(),
            RecipientUserId = recipient,
            ActorUserId = actorUserId,
            Type = "NewPendingMission",
            Title = "New Pending Mission",
            Message = $"Mission '{missionName}' has been submitted and is waiting for approval.",
            ReferenceId = missionId,
            ReferenceType = "Mission",
            ActionUrl = $"/validation-approvals?missionId={missionId}",
            CreatedAt = DateTimeOffset.UtcNow
        });
        await repository.AddRangeAsync(notifications, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
    }

    public async Task PublishMissionDecisionAsync(Guid missionId, Guid actorUserId, bool approved, CancellationToken cancellationToken = default)
    {
        var mission = await missionRepository.GetByIdAsync(missionId, cancellationToken);
        if (mission is null) return;
        var decision = approved ? "approved" : "rejected";
        await repository.AddAsync(new Notification
        {
            Id = Guid.NewGuid(),
            RecipientUserId = mission.CreatedBy,
            ActorUserId = actorUserId,
            Type = approved ? "MissionApproved" : "MissionRejected",
            Title = approved ? "Mission Approved" : "Mission Rejected",
            Message = $"Mission '{mission.Name}' has been {decision}.",
            ReferenceId = mission.Id,
            ReferenceType = "Mission",
            ActionUrl = $"/missions/{mission.Id}",
            CreatedAt = DateTimeOffset.UtcNow
        }, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
    }

    private static NotificationResponse Map(Notification notification) => new()
    {
        Id = notification.Id,
        RecipientUserId = notification.RecipientUserId,
        ActorUserId = notification.ActorUserId,
        Type = notification.Type,
        Title = notification.Title,
        Message = notification.Message,
        ReferenceId = notification.ReferenceId,
        ReferenceType = notification.ReferenceType,
        ActionUrl = notification.ActionUrl,
        IsRead = notification.IsRead,
        ReadAt = notification.ReadAt,
        CreatedAt = notification.CreatedAt
    };
}
