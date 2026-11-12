using DroneOps.Application.Interfaces.Storage;
using DroneOps.Application.Interfaces.UAVs;
using DroneOps.Application.Interfaces.Notifications;
using DroneOps.Persistence.Data;
using Microsoft.EntityFrameworkCore;

namespace DroneOps.API;

public sealed class UavImageModerationMonitor(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<UavImageModerationMonitor> logger) : BackgroundService
{
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromSeconds(15),
        TimeSpan.FromSeconds(45),
        TimeSpan.FromSeconds(90),
        TimeSpan.FromMinutes(4)
    ];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Clamp(
            configuration.GetValue("UavImageModeration:WorkerIntervalSeconds", 5), 2, 60));

        var idleDelay = new IdlePollDelay(interval, TimeSpan.FromSeconds(60));
        while (!stoppingToken.IsCancellationRequested)
        {
            var hadWork = true;
            try
            {
                hadWork = await ProcessDueImagesAsync(stoppingToken);
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(exception, "UAV image moderation worker iteration failed.");
            }

            await Task.Delay(idleDelay.Next(hadWork), stoppingToken);
        }
    }

    private async Task<bool> ProcessDueImagesAsync(CancellationToken cancellationToken)
    {
        await using var discoveryScope = scopeFactory.CreateAsyncScope();
        var discoveryDb = discoveryScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTimeOffset.UtcNow;
        var dueIds = await discoveryDb.UAVs.AsNoTracking()
            .Where(x => x.ImageStoragePath != null &&
                (x.ImageModerationStatus == "Pending" ||
                 x.ImageModerationStatus == "RetryScheduled" ||
                 x.ImageModerationStatus == "Processing") &&
                (x.ImageModerationNextAttemptAt == null || x.ImageModerationNextAttemptAt <= now))
            .OrderBy(x => x.ImageModerationNextAttemptAt)
            .Select(x => x.Id)
            .Take(5)
            .ToListAsync(cancellationToken);

        foreach (var id in dueIds)
        {
            await ProcessOneAsync(id, cancellationToken);
        }
        return dueIds.Count > 0;
    }

    private async Task ProcessOneAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTimeOffset.UtcNow;
        var claimed = await db.UAVs
            .Where(x => x.Id == id && x.ImageStoragePath != null &&
                (x.ImageModerationStatus == "Pending" ||
                 x.ImageModerationStatus == "RetryScheduled" ||
                 x.ImageModerationStatus == "Processing") &&
                (x.ImageModerationNextAttemptAt == null || x.ImageModerationNextAttemptAt <= now))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.ImageModerationStatus, "Processing")
                .SetProperty(x => x.ImageModerationAttempts, x => x.ImageModerationAttempts + 1)
                .SetProperty(x => x.ImageModerationNextAttemptAt, now.AddMinutes(2)),
                cancellationToken);
        if (claimed != 1) return;

        var image = await db.UAVs.AsNoTracking().SingleAsync(x => x.Id == id, cancellationToken);
        var path = image.ImageStoragePath!;
        UavImageModerationResult result;
        try
        {
            var storage = scope.ServiceProvider.GetRequiredService<IUserMediaStorage>();
            var moderation = scope.ServiceProvider.GetRequiredService<IUavImageModerationService>();
            var bytes = await storage.DownloadAsync(path, cancellationToken);
            result = await moderation.ModerateAsync(
                bytes, image.ImageContentType ?? "application/octet-stream", cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "UAV image {UavId} could not be moderated on attempt {Attempt}.",
                id, image.ImageModerationAttempts);
            result = new(UavImageModerationDecision.Unavailable, "Moderation dependency failed temporarily.");
        }

        var maximumAttempts = Math.Clamp(
            configuration.GetValue("UavImageModeration:MaximumAttempts", 5), 1, 10);
        var completedAt = DateTimeOffset.UtcNow;
        var status = result.Decision switch
        {
            UavImageModerationDecision.Approved => "Approved",
            UavImageModerationDecision.Rejected => "Rejected",
            _ when image.ImageModerationAttempts >= maximumAttempts => "ManualReview",
            _ => "RetryScheduled"
        };
        DateTimeOffset? nextAttempt = status == "RetryScheduled"
            ? completedAt.Add(RetryDelays[Math.Min(image.ImageModerationAttempts - 1, RetryDelays.Length - 1)])
            : null;
        var reason = result.Reason.Length <= 500 ? result.Reason : result.Reason[..500];

        var updated = await db.UAVs.Where(x => x.Id == id && x.ImageStoragePath == path && x.ImageModerationStatus == "Processing")
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.ImageModerationStatus, status)
                .SetProperty(x => x.ImageModerationNextAttemptAt, nextAttempt)
                .SetProperty(x => x.ImageModeratedAt, status == "RetryScheduled" ? null : completedAt)
                .SetProperty(x => x.ImageModerationReason, reason), cancellationToken);

        if (updated == 1 && status == "ManualReview")
        {
            var notifications = scope.ServiceProvider.GetRequiredService<INotificationService>();
            await notifications.RefreshUavManualReviewAsync(cancellationToken);
        }
        else if (updated == 1 && status == "Rejected")
        {
            // A rejected image needs action from its owner, not from every manager.
            var recipientUserId = await db.Pilots.AsNoTracking()
                .Where(x => x.Id == image.PilotId)
                .Select(x => x.UserId)
                .SingleAsync(cancellationToken);
            var notifications = scope.ServiceProvider.GetRequiredService<INotificationService>();
            await notifications.PublishToUserAsync(recipientUserId, "UavImageRejected",
                "UAV Image Rejected",
                $"The image for UAV '{image.Code} - {image.Name}' could not be verified. Please replace it.",
                null, image.Id, "UAV", $"/portal/uav/{image.Id}/edit", cancellationToken);
        }
    }
}
