using DroneOps.Application.Interfaces.Notifications;
using DroneOps.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace DroneOps.Application.Services.UAVs;

/// <summary>
/// Isolates best-effort UAV notification publishing so notification outages never fail core UAV persistence.
/// </summary>
internal sealed class UavNotificationHelper(
    INotificationService notificationService,
    ILogger logger)
{
    public async Task TryPublishToManagersAsync(
        string type,
        string title,
        string message,
        Guid actorUserId,
        Guid uavId,
        CancellationToken cancellationToken)
    {
        try
        {
            await notificationService.PublishToManagersAsync(
                type,
                title,
                message,
                actorUserId,
                uavId,
                "UAV",
                $"/uav-management?uavId={uavId}",
                cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Could not publish UAV notification for {UavId}.", uavId);
        }
    }

    public async Task TryPublishDecisionAsync(
        UAV uav,
        bool approved,
        CancellationToken cancellationToken)
    {
        try
        {
            var recipientUserId = uav.Pilot.UserId;
            var type = approved ? "UavApproved" : "UavRejected";
            var title = approved ? "UAV Approved" : "UAV Rejected";
            var message = approved
                ? $"Your UAV '{uav.Code} - {uav.Name}' has been approved and is now operational."
                : $"Your UAV '{uav.Code} - {uav.Name}' request was rejected.";

            await notificationService.PublishToUserAsync(
                recipientUserId,
                type,
                title,
                message,
                null,
                uav.Id,
                "UAV",
                $"/uav-management?uavId={uav.Id}",
                cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Could not publish UAV decision notification for {UavId}.", uav.Id);
        }
    }

    public async Task TryPublishImageRejectedAsync(
        UAV uav,
        Guid managerUserId,
        string normalizedReason,
        CancellationToken cancellationToken)
    {
        try
        {
            await notificationService.PublishToUserAsync(
                uav.Pilot.UserId,
                "UavImageRejected",
                "UAV Image Rejected",
                $"The image for UAV '{uav.Code} - {uav.Name}' was removed by a manager. Reason: {normalizedReason}",
                managerUserId,
                uav.Id,
                "UAV",
                $"/portal/uav/{uav.Id}/edit",
                cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Could not publish image rejection for UAV {UavId}.", uav.Id);
        }
    }
}
