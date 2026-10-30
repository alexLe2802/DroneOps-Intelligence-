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

    public async Task PublishToManagersAsync(
        string type, string title, string message, Guid? actorUserId, Guid referenceId,
        string referenceType, string actionUrl, CancellationToken cancellationToken = default)
    {
        var recipients = await repository.GetActiveOperationManagerIdsAsync(cancellationToken);
        await AddForRecipientsAsync(recipients, type, title, message, actorUserId,
            referenceId, referenceType, actionUrl, cancellationToken);
    }

    public async Task PublishToUserAsync(
        Guid recipientUserId, string type, string title, string message, Guid? actorUserId,
        Guid referenceId, string referenceType, string actionUrl, CancellationToken cancellationToken = default)
    {
        await repository.AddAsync(Create(recipientUserId, type, title, message, actorUserId,
            referenceId, referenceType, actionUrl), cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
    }

    public async Task PublishIncidentRaisedAsync(
        Guid incidentId, string incidentType, Guid pilotUserId, CancellationToken cancellationToken = default)
    {
        var managers = await repository.GetActiveOperationManagerIdsAsync(cancellationToken);
        var recipients = managers.Append(pilotUserId).Distinct();
        await AddForRecipientsAsync(recipients, "IncidentRaised", $"Incident Alert: {incidentType}",
            $"A new {incidentType} incident has been raised.", null, incidentId,
            "Incident", $"/incident-management?incidentId={incidentId}", cancellationToken);
    }

    public async Task PublishPendingMissionAsync(Guid missionId, Guid actorUserId, string missionName, CancellationToken cancellationToken = default)
    {
        await PublishToManagersAsync("NewPendingMission", "New Pending Mission",
            $"Mission '{missionName}' has been submitted and is waiting for approval.",
            actorUserId, missionId, "Mission", $"/validation-approvals?missionId={missionId}", cancellationToken);
    }

    public async Task PublishMissionDecisionAsync(Guid missionId, Guid actorUserId, bool approved, string? rejectionReason = null, CancellationToken cancellationToken = default)
    {
        var mission = await missionRepository.GetByIdAsync(missionId, cancellationToken);
        if (mission is null) return;
        var message = approved
            ? $"Your mission '{mission.Name}' has been approved."
            : $"Your mission '{mission.Name}' was rejected." +
              (string.IsNullOrWhiteSpace(rejectionReason) ? string.Empty : $" Reason: {rejectionReason.Trim()}");
        await PublishToUserAsync(mission.CreatedBy,
            approved ? "MissionApproved" : "MissionRejected",
            approved ? "Mission Approved" : "Mission Rejected", message, actorUserId,
            mission.Id, "Mission", $"/mission-planning?missionId={mission.Id}", cancellationToken);
    }

    private async Task AddForRecipientsAsync(
        IEnumerable<Guid> recipients, string type, string title, string message, Guid? actorUserId,
        Guid referenceId, string referenceType, string actionUrl, CancellationToken cancellationToken)
    {
        var notifications = recipients.Distinct().Select(recipient =>
            Create(recipient, type, title, message, actorUserId, referenceId, referenceType, actionUrl));
        await repository.AddRangeAsync(notifications, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
    }

    private static Notification Create(
        Guid recipientUserId, string type, string title, string message, Guid? actorUserId,
        Guid referenceId, string referenceType, string actionUrl) => new()
    {
        Id = Guid.NewGuid(),
        RecipientUserId = recipientUserId,
        ActorUserId = actorUserId,
        Type = type,
        Title = title,
        Message = message,
        ReferenceId = referenceId,
        ReferenceType = referenceType,
        ActionUrl = actionUrl,
        CreatedAt = DateTimeOffset.UtcNow
    };

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
